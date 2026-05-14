using System.Text;
using System.Text.Json;
using Packet.AprsIs.Spike;
using Packet.Ax25;
using Packet.Core;

// APRS-IS UI-frame ingestion spike (SP-001b).
//
// Connects to APRS-IS as a read-only listener, parses received TNC2-format
// lines, attempts to reconstruct each as an Ax25Frame.Ui(...), round-trips
// through our parser, and records both successes and failures to:
//
//   artifacts/aprs-is-spike/<timestamp>/failures.jsonl
//   artifacts/aprs-is-spike/<timestamp>/stats.md
//
// The purpose isn't to act as an APRS gateway — it's to exercise our
// AX.25 address/frame plumbing against a steady stream of real-world data
// and surface edge cases that synthetic tests don't cover.
//
// Args:
//   --server <host:port>     default rotate.aprs2.net:14580
//   --callsign <CALL[-SSID]> default N0CALL
//   --filter <filter>        default "t/poimqstuc" (all APRS payload types)
//   --max-frames <N>         default 1000  (stop after this many; 0 = infinite)
//   --out-dir <dir>          default artifacts/aprs-is-spike/<timestamp>/
//   --quiet                  suppress per-frame stdout (only summary)

var opts = ParseArgs(args);

Directory.CreateDirectory(opts.OutDir);
string failuresPath = Path.Combine(opts.OutDir, "failures.jsonl");
string statsPath = Path.Combine(opts.OutDir, "stats.md");

Console.Error.WriteLine($"# Packet.AprsIs.Spike → out-dir: {opts.OutDir}");
Console.Error.WriteLine($"# server: {opts.Host}:{opts.Port}  callsign: {opts.Callsign}  filter: {opts.Filter}");
Console.Error.WriteLine($"# max-frames: {(opts.MaxFrames == 0 ? "unlimited" : opts.MaxFrames.ToString())}");

var stats = new Stats();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.Error.WriteLine("# ^C — finishing up...");
    cts.Cancel();
};

using var failuresFile = File.CreateText(failuresPath);

await using var client = new AprsIsClient();
try
{
    await client.ConnectAsync(opts.Host, opts.Port, opts.Callsign, -1, opts.Filter, cts.Token);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"# connect failed: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

DateTime startedAt = DateTime.UtcNow;

await foreach (var line in client.ReadLinesAsync(cts.Token))
{
    stats.LinesReceived++;

    // Comments / server messages.
    if (line.Length == 0 || line[0] == '#')
    {
        stats.NonFrameLines++;
        continue;
    }

    if (!Tnc2Parser.TryParse(line, out var parsed))
    {
        stats.Tnc2ParseFailures++;
        await LogFailure("tnc2_parse_failed", line, null, null);
        continue;
    }

    stats.FramesParsed++;

    // Reconstruct as an AX.25 UI frame.
    if (!TryReconstruct(parsed, out var frame, out string? reconstructError))
    {
        stats.ReconstructFailures++;
        stats.TallyReconstructError(reconstructError ?? "unknown");
        await LogFailure("reconstruct_failed", line, reconstructError, parsed);
        continue;
    }

    // Round-trip: encode then decode, assert the decoded form structurally
    // matches what we just built.
    byte[] bytes = frame.ToBytes();
    if (!Ax25Frame.TryParse(bytes, out var decoded))
    {
        stats.RoundTripFailures++;
        await LogFailure("roundtrip_parse_failed", line, $"TryParse rejected {bytes.Length}-byte encoding", parsed);
        continue;
    }

    if (!StructurallyEqual(frame, decoded!))
    {
        stats.RoundTripFailures++;
        await LogFailure("roundtrip_mismatch", line,
            $"decoded didn't match: dest={decoded!.Destination.Callsign} src={decoded.Source.Callsign} info={decoded.Info.Length}B",
            parsed);
        continue;
    }

    stats.RoundTripSuccesses++;
    if (!opts.Quiet)
    {
        Console.WriteLine($"✓ {parsed.Source} > {parsed.Destination} ({parsed.Info.Length}B)");
    }

    if (opts.MaxFrames > 0 && stats.FramesParsed >= opts.MaxFrames)
    {
        Console.Error.WriteLine($"# reached --max-frames {opts.MaxFrames}, stopping");
        break;
    }
}

DateTime endedAt = DateTime.UtcNow;

// Write final stats.md
await File.WriteAllTextAsync(statsPath, stats.RenderMarkdown(opts, startedAt, endedAt));

Console.Error.WriteLine();
Console.Error.WriteLine($"# summary written to {statsPath}");
Console.Error.WriteLine($"# failures jsonl: {failuresPath} ({stats.TotalFailures} entries)");
Console.Error.WriteLine();
Console.Error.WriteLine(stats.RenderShortSummary());

return 0;

// ────────────────────────────────────────────────────────────────────

async Task LogFailure(string kind, string raw, string? reason, Tnc2Parser.Tnc2Line? parsed)
{
    var record = new
    {
        kind,
        raw,
        reason,
        parsed = parsed is null ? null : new
        {
            source = parsed.Source,
            destination = parsed.Destination,
            digipeaters = parsed.Digipeaters,
            info_first_bytes = HexFirstN(parsed.Info, 16),
            info_length = parsed.Info.Length,
        },
        at = DateTime.UtcNow,
    };
    await failuresFile.WriteLineAsync(JsonSerializer.Serialize(record));
    await failuresFile.FlushAsync();
}

static string HexFirstN(ReadOnlyMemory<byte> data, int n)
{
    int len = Math.Min(data.Length, n);
    var sb = new StringBuilder(len * 2);
    var span = data.Span;
    for (int i = 0; i < len; i++) sb.Append(span[i].ToString("x2"));
    return sb.ToString();
}

static bool TryReconstruct(Tnc2Parser.Tnc2Line parsed, out Ax25Frame frame, out string? error)
{
    frame = null!;
    error = null;

    if (!Callsign.TryParse(parsed.Source, out var src))
    {
        error = $"invalid source callsign: '{parsed.Source}'";
        return false;
    }
    if (!Callsign.TryParse(parsed.Destination, out var dst))
    {
        error = $"invalid destination callsign: '{parsed.Destination}'";
        return false;
    }

    // Filter out Q-construct pseudo-digipeaters. They're routing metadata
    // from APRS-IS, not real on-air hops, and AX.25 caps the digi list at 8
    // anyway.
    var digiCalls = new List<Callsign>();
    foreach (var entry in parsed.Digipeaters)
    {
        if (Tnc2Parser.IsQConstruct(entry.Callsign)) break;
        if (!Callsign.TryParse(entry.Callsign, out var digi))
        {
            error = $"invalid digipeater callsign: '{entry.Callsign}'";
            return false;
        }
        digiCalls.Add(digi);
        if (digiCalls.Count >= 8) break;
    }

    try
    {
        frame = Ax25Frame.Ui(
            destination: dst,
            source: src,
            info: parsed.Info.Span,
            digipeaters: digiCalls);
        return true;
    }
    catch (Exception ex)
    {
        error = $"{ex.GetType().Name}: {ex.Message}";
        return false;
    }
}

static bool StructurallyEqual(Ax25Frame built, Ax25Frame decoded)
{
    if (built.Destination.Callsign != decoded.Destination.Callsign) return false;
    if (built.Source.Callsign != decoded.Source.Callsign) return false;
    if (built.Digipeaters.Count != decoded.Digipeaters.Count) return false;
    for (int i = 0; i < built.Digipeaters.Count; i++)
    {
        if (built.Digipeaters[i].Callsign != decoded.Digipeaters[i].Callsign) return false;
    }
    if (built.Control != decoded.Control) return false;
    if (built.Pid != decoded.Pid) return false;
    if (!built.Info.Span.SequenceEqual(decoded.Info.Span)) return false;
    return true;
}

static Options ParseArgs(string[] args)
{
    var opts = new Options
    {
        Host = "rotate.aprs2.net",
        Port = 14580,
        Callsign = "N0CALL",
        Filter = "t/poimqstuc",
        MaxFrames = 1000,
        OutDir = Path.Combine("artifacts", "aprs-is-spike", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")),
        Quiet = false,
    };

    for (int i = 0; i < args.Length; i++)
    {
        string a = args[i];
        string? next() => i + 1 < args.Length ? args[++i] : null;
        switch (a)
        {
            case "--server":
                var sv = next();
                if (sv is null) throw new ArgumentException("--server requires host:port");
                var parts = sv.Split(':');
                opts.Host = parts[0];
                opts.Port = parts.Length > 1 ? int.Parse(parts[1]) : 14580;
                break;
            case "--callsign":
                opts.Callsign = next() ?? throw new ArgumentException("--callsign requires value");
                break;
            case "--filter":
                opts.Filter = next() ?? throw new ArgumentException("--filter requires value");
                break;
            case "--max-frames":
                opts.MaxFrames = int.Parse(next() ?? throw new ArgumentException("--max-frames requires value"));
                break;
            case "--out-dir":
                opts.OutDir = next() ?? throw new ArgumentException("--out-dir requires value");
                break;
            case "--quiet":
                opts.Quiet = true;
                break;
            default:
                throw new ArgumentException($"unknown arg: {a}");
        }
    }
    return opts;
}

sealed class Options
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Callsign { get; set; } = "";
    public string Filter { get; set; } = "";
    public int MaxFrames { get; set; }
    public string OutDir { get; set; } = "";
    public bool Quiet { get; set; }
}

sealed class Stats
{
    public int LinesReceived { get; set; }
    public int NonFrameLines { get; set; }
    public int FramesParsed { get; set; }
    public int Tnc2ParseFailures { get; set; }
    public int ReconstructFailures { get; set; }
    public int RoundTripFailures { get; set; }
    public int RoundTripSuccesses { get; set; }

    public Dictionary<string, int> ReconstructErrorKinds { get; } = new(StringComparer.Ordinal);

    public int TotalFailures => Tnc2ParseFailures + ReconstructFailures + RoundTripFailures;

    public void TallyReconstructError(string err)
    {
        // Bucket the error message to a coarse kind for the histogram.
        string kind =
            err.StartsWith("invalid source callsign", StringComparison.Ordinal) ? "invalid_source" :
            err.StartsWith("invalid destination callsign", StringComparison.Ordinal) ? "invalid_destination" :
            err.StartsWith("invalid digipeater callsign", StringComparison.Ordinal) ? "invalid_digipeater" :
            "other";
        ReconstructErrorKinds.TryGetValue(kind, out int n);
        ReconstructErrorKinds[kind] = n + 1;
    }

    public string RenderShortSummary()
    {
        double pct(int x) => FramesParsed == 0 ? 0 : 100.0 * x / FramesParsed;
        return $@"  lines received     : {LinesReceived}
  non-frame lines    : {NonFrameLines}
  frames parsed      : {FramesParsed}
  tnc2 parse failures: {Tnc2ParseFailures}
  reconstruct failed : {ReconstructFailures} ({pct(ReconstructFailures):F1}% of parsed)
  round-trip failed  : {RoundTripFailures} ({pct(RoundTripFailures):F1}% of parsed)
  round-trip succeed : {RoundTripSuccesses} ({pct(RoundTripSuccesses):F1}% of parsed)";
    }

    public string RenderMarkdown(Options opts, DateTime started, DateTime ended)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# APRS-IS spike — stats");
        sb.AppendLine();
        sb.AppendLine($"- **Server**: `{opts.Host}:{opts.Port}`");
        sb.AppendLine($"- **Callsign**: `{opts.Callsign}` (read-only)");
        sb.AppendLine($"- **Filter**: `{opts.Filter}`");
        sb.AppendLine($"- **Started**: {started:O}");
        sb.AppendLine($"- **Ended**: {ended:O}");
        sb.AppendLine($"- **Duration**: {ended - started:c}");
        sb.AppendLine();
        sb.AppendLine("## Counts");
        sb.AppendLine();
        sb.AppendLine("| Metric | Count |");
        sb.AppendLine("|---|--:|");
        sb.AppendLine($"| Lines received | {LinesReceived} |");
        sb.AppendLine($"| Non-frame lines (server messages, keepalives) | {NonFrameLines} |");
        sb.AppendLine($"| Frames parsed (TNC2-level) | {FramesParsed} |");
        sb.AppendLine($"| TNC2 parse failures | {Tnc2ParseFailures} |");
        sb.AppendLine($"| Reconstruct (TNC2 → Ax25Frame.Ui) failures | {ReconstructFailures} |");
        sb.AppendLine($"| Round-trip (encode → decode → equal) failures | {RoundTripFailures} |");
        sb.AppendLine($"| Round-trip successes | {RoundTripSuccesses} |");

        if (ReconstructErrorKinds.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Reconstruct failure breakdown");
            sb.AppendLine();
            sb.AppendLine("| Kind | Count |");
            sb.AppendLine("|---|--:|");
            foreach (var kv in ReconstructErrorKinds.OrderByDescending(x => x.Value))
            {
                sb.AppendLine($"| `{kv.Key}` | {kv.Value} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Notes");
        sb.AppendLine();
        sb.AppendLine("- Q-construct pseudo-digipeaters (`qAR`/`qAS`/`qAo`/…) are dropped before reconstruct attempt.");
        sb.AppendLine("- Reconstruct failures are *interesting* — they're real-world data our parser/builder can't represent. See `failures.jsonl` for the raw lines.");
        sb.AppendLine("- This spike doesn't validate the APRS payload itself, only that the AX.25 envelope round-trips.");

        return sb.ToString();
    }
}
