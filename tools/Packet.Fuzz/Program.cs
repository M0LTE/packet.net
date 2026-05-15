using System.Diagnostics;
using System.Globalization;
using System.Text;
using Packet.Ax25;
using Packet.Kiss;
using SharpFuzz;

namespace Packet.Fuzz;

/// <summary>
/// SP-004 — SharpFuzz harness for the AX.25 and KISS wire-format parsers.
/// </summary>
/// <remarks>
/// <para>
/// Two parser targets:
/// </para>
/// <list type="bullet">
/// <item><c>Ax25Frame.TryParse(ReadOnlySpan&lt;byte&gt;, out _)</c> — the direct AX.25
/// KISS-form (no flags, no FCS) parser.</item>
/// <item><c>KissDecoder.Push(ReadOnlySpan&lt;byte&gt;)</c> — KISS parser entry. The
/// task brief asked for <c>KissFrame.TryParse</c> but no such method exists; KISS
/// is a stateful framer, not a one-shot parser, so the equivalent harness drives
/// arbitrary byte sequences through <see cref="KissDecoder"/> instead.</item>
/// </list>
/// <para>Usage:</para>
/// <code>
///   dotnet run --project tools/Packet.Fuzz -- --smoke [N]
///   dotnet run --project tools/Packet.Fuzz -- ax25 [corpus-dir]
///   dotnet run --project tools/Packet.Fuzz -- kiss [corpus-dir]
/// </code>
/// <para>
/// <c>--smoke</c> is the always-works mode: it generates N random / structured
/// inputs in-process and asserts neither <c>TryParse</c> escapes an exception.
/// The <c>ax25</c> / <c>kiss</c> subcommands invoke
/// <see cref="Fuzzer.OutOfProcess.Run"/> for use under <c>afl-fuzz</c> +
/// <c>libfuzzer-dotnet</c>; they aren't required for the smoke pass.
/// </para>
/// </remarks>
public static class Program
{
    private const int DefaultSmokeIterations = 1000;

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        return args[0] switch
        {
            "--smoke"        => RunSmoke(args),
            "--seed-corpus"  => RunSeedCorpus(args),
            "ax25"           => RunAx25Fuzzer(args),
            "kiss"           => RunKissFuzzer(args),
            "--help"
                or "-h"      => RunHelp(),
            _ => UnknownCommand(args[0]),
        };
    }

    private static int RunHelp()
    {
        PrintUsage();
        return 0;
    }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: {cmd}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Packet.Fuzz — SP-004 frame-parser fuzzing harness");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  Packet.Fuzz --smoke [N] [seed]     Smoke test (default N=1000) covering both parsers; optional RNG seed.");
        Console.WriteLine("  Packet.Fuzz --seed-corpus [dir]    Write the known-valid seed corpus files into <dir>/ax25 + <dir>/kiss.");
        Console.WriteLine("  Packet.Fuzz ax25 [corpus-dir]      AFL/libfuzzer harness for Ax25Frame.TryParse.");
        Console.WriteLine("  Packet.Fuzz kiss [corpus-dir]      AFL/libfuzzer harness for KissDecoder.Push.");
    }

    // ─── seed corpus ──────────────────────────────────────────────────

    private static int RunSeedCorpus(string[] args)
    {
        string root = args.Length >= 2
            ? args[1]
            : Path.Combine(AppContext.BaseDirectory, "corpus");
        Directory.CreateDirectory(Path.Combine(root, "ax25"));
        Directory.CreateDirectory(Path.Combine(root, "kiss"));

        Console.WriteLine($"Writing seed corpus under {root}…");

        // ── AX.25 seeds (in KISS form: no flags, no FCS) ─────────────
        var ax25Seeds = new (string Name, byte[] Bytes)[]
        {
            ("sabm.bin",       Ax25Frame.Sabm(Cs("MOLTER", 0), Cs("M0LTE",  7)).ToBytes()),
            ("ua.bin",         Ax25Frame.Ua  (Cs("M0LTE",  7), Cs("MOLTER", 0)).ToBytes()),
            ("disc.bin",       Ax25Frame.Disc(Cs("MOLTER", 0), Cs("M0LTE",  7)).ToBytes()),
            ("ui-aprs.bin",    Ax25Frame.Ui  (Cs("APRS",   0), Cs("M0LTE",  7),
                                              info: System.Text.Encoding.ASCII.GetBytes("!5126.30N/00121.30W>"),
                                              pid:  Ax25Frame.PidNoLayer3).ToBytes()),
            ("i-frame.bin",    Ax25Frame.I   (Cs("MOLTER", 0), Cs("M0LTE",  7),
                                              nr: 3, ns: 5,
                                              info: System.Text.Encoding.ASCII.GetBytes("hello world"),
                                              pid:  Ax25Frame.PidNoLayer3).ToBytes()),
            ("rr.bin",         Ax25Frame.Rr  (Cs("MOLTER", 0), Cs("M0LTE",  7),
                                              nr: 0, isCommand: true).ToBytes()),
        };
        foreach (var (name, bytes) in ax25Seeds)
        {
            string path = Path.Combine(root, "ax25", name);
            File.WriteAllBytes(path, bytes);
            Console.WriteLine($"  ax25/{name} — {bytes.Length} bytes");
        }

        // ── KISS seeds (full SLIP-framed FEND…FEND) ──────────────────
        // Each is the canonical "data frame on port 0" shape: FEND, command 0x00,
        // payload, FEND. The payload is one of the AX.25 seeds above.
        foreach (var (name, ax25Bytes) in ax25Seeds)
        {
            byte[] kiss = WrapKissDataFrame(port: 0, ax25Bytes);
            string path = Path.Combine(root, "kiss", Path.ChangeExtension(name, ".kiss.bin"));
            File.WriteAllBytes(path, kiss);
            Console.WriteLine($"  kiss/{Path.GetFileName(path)} — {kiss.Length} bytes");
        }

        return 0;
    }

    private static Packet.Core.Callsign Cs(string @base, byte ssid)
        => new(@base, ssid);

    /// <summary>
    /// Wrap an AX.25 frame in a single KISS data frame: <c>FEND, (port&lt;&lt;4)|cmd, payload (escaped), FEND</c>.
    /// </summary>
    private static byte[] WrapKissDataFrame(byte port, byte[] payload)
    {
        const byte Fend  = 0xC0;
        const byte Fesc  = 0xDB;
        const byte Tfend = 0xDC;
        const byte Tfesc = 0xDD;
        var ms = new MemoryStream(payload.Length + 4);
        ms.WriteByte(Fend);
        ms.WriteByte((byte)((port & 0x0F) << 4));   // command 0x0 = Data
        foreach (byte b in payload)
        {
            switch (b)
            {
                case Fend: ms.WriteByte(Fesc); ms.WriteByte(Tfend); break;
                case Fesc: ms.WriteByte(Fesc); ms.WriteByte(Tfesc); break;
                default:   ms.WriteByte(b); break;
            }
        }
        ms.WriteByte(Fend);
        return ms.ToArray();
    }

    // ─── smoke ────────────────────────────────────────────────────────

    private static int RunSmoke(string[] args)
    {
        int iterations = DefaultSmokeIterations;
        int seed = unchecked((int)0xC0DEFEED);
        if (args.Length >= 2 && !int.TryParse(args[1], out iterations))
        {
            Console.Error.WriteLine($"Bad iteration count: {args[1]}");
            return 1;
        }
        if (args.Length >= 3 && !int.TryParse(args[2], out seed))
        {
            Console.Error.WriteLine($"Bad seed: {args[2]}");
            return 1;
        }

        Console.WriteLine($"Packet.Fuzz smoke run: {iterations} iterations per parser, seed=0x{seed:X8}");
        Console.WriteLine();

        // Always replay the on-disk seed corpus first so the smoke run is at
        // least as broad as the AFL seed set — any throw on a known-valid
        // sample is a regression we want to catch in CI.
        var ax25Seeds = LoadCorpus("ax25");
        var kissSeeds = LoadCorpus("kiss");

        var ax25 = SmokeOne("Ax25Frame.TryParse", iterations, FuzzAx25Bytes, ax25Seeds, seed);
        Console.WriteLine();
        var kiss = SmokeOne("KissDecoder.Push", iterations, FuzzKissBytes, kissSeeds, seed);

        Console.WriteLine();
        Console.WriteLine("════════ Summary ════════");
        Report(ax25);
        Report(kiss);

        return (ax25.Findings.Count + kiss.Findings.Count) == 0 ? 0 : 2;
    }

    private static byte[][] LoadCorpus(string subdir)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "corpus", subdir);
        if (!Directory.Exists(dir))
        {
            return [];
        }
        return [.. Directory.EnumerateFiles(dir).Select(File.ReadAllBytes)];
    }

    private static SmokeResult SmokeOne(string label, int iterations, Action<byte[]> target, byte[][] seeds, int rngSeed)
    {
        Console.WriteLine($"── {label} ──");
        var rng = new Random(rngSeed);
        var stopwatch = Stopwatch.StartNew();
        var result = new SmokeResult(label, iterations);

        // 1) Replay the seed corpus verbatim. These are known-valid frames;
        //    parsing them must succeed and not throw.
        foreach (var seed in seeds)
        {
            TryRun(target, seed, result);
        }

        // 2) Bit-flip + byte-replacement mutations of each seed (one mutation
        //    per pass, several passes). Cheap way to probe near-valid inputs
        //    that the structural random generator may not reach.
        const int mutationsPerSeed = 32;
        foreach (var seed in seeds)
        {
            for (int m = 0; m < mutationsPerSeed; m++)
            {
                TryRun(target, MutateOnce(rng, seed), result);
            }
        }

        // 3) Bulk random / structured inputs.
        for (int i = 0; i < iterations; i++)
        {
            byte[] input = GenerateInput(rng, i);
            TryRun(target, input, result);
        }

        stopwatch.Stop();
        int totalInputs = seeds.Length + seeds.Length * mutationsPerSeed + iterations;
        Console.WriteLine($"  {totalInputs} inputs ({seeds.Length} seed + {seeds.Length * mutationsPerSeed} seed-mutations + {iterations} generated) / {stopwatch.ElapsedMilliseconds} ms / {result.Findings.Count} unhandled exceptions");
        return result;
    }

    private static void TryRun(Action<byte[]> target, byte[] input, SmokeResult result)
    {
        try
        {
            target(input);
        }
#pragma warning disable CA1031 // catch general Exception: the whole point of fuzzing is to find escaping exceptions
        catch (Exception ex)
#pragma warning restore CA1031
        {
            result.Findings.Add(new Finding(input, ex));
        }
    }

    private static byte[] MutateOnce(Random rng, byte[] seed)
    {
        if (seed.Length == 0)
        {
            return RandomBuffer(rng, rng.Next(0, 4));
        }
        var copy = (byte[])seed.Clone();
        int pos = rng.Next(copy.Length);
        copy[pos] = rng.Next(4) switch
        {
            0 => (byte)(copy[pos] ^ (1 << rng.Next(8))), // bit flip
            1 => (byte)rng.Next(256),                     // byte replace
            2 => 0,                                       // zero
            _ => 0xFF,                                    // 0xFF
        };
        return copy;
    }

    private static void Report(SmokeResult result)
    {
        if (result.Findings.Count == 0)
        {
            Console.WriteLine($"  {result.Label}: clean — {result.Iterations} inputs, no throws.");
            return;
        }

        Console.WriteLine($"  {result.Label}: {result.Findings.Count} unhandled exception(s):");
        // Group by exception type + outer frame to keep output digestible.
        var groups = result.Findings
            .GroupBy(f => f.Exception.GetType().FullName + "|" + FirstStackFrame(f.Exception))
            .OrderByDescending(g => g.Count());
        foreach (var g in groups)
        {
            var sample = g.First();
            Console.WriteLine($"    × {g.Count()}  {sample.Exception.GetType().Name}: {sample.Exception.Message}");
            Console.WriteLine($"           at {FirstStackFrame(sample.Exception)}");
            Console.WriteLine($"           sample input ({sample.Input.Length} bytes): {ToHex(sample.Input, max: 64)}");
        }
    }

    private static string FirstStackFrame(Exception ex)
    {
        var trace = new StackTrace(ex, fNeedFileInfo: false);
        var frame = trace.GetFrame(0);
        if (frame is null)
        {
            return "(no frame)";
        }
        var method = frame.GetMethod();
        if (method is null)
        {
            return "(no method)";
        }
        return $"{method.DeclaringType?.FullName}.{method.Name}";
    }

    private static string ToHex(byte[] bytes, int max)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        int n = Math.Min(bytes.Length, max);
        for (int i = 0; i < n; i++)
        {
            sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }
        if (bytes.Length > max)
        {
            sb.Append('…');
        }
        return sb.ToString();
    }

    // ─── input generation ─────────────────────────────────────────────

    /// <summary>
    /// Generate a random byte buffer biased toward shapes likely to surface
    /// parser edge cases: short buffers, lengths near the minimum frame size,
    /// long payloads, structured-looking frames, and pathological all-same-byte
    /// inputs that probe the framing layer.
    /// </summary>
    private static byte[] GenerateInput(Random rng, int i)
    {
        // Mix seven strategies so the corpus covers small, near-threshold,
        // long, structured, and pathological inputs evenly:
        return (i % 7) switch
        {
            0 => RandomBuffer(rng, rng.Next(0, 16)),                 // truncated
            1 => RandomBuffer(rng, rng.Next(14, 32)),                // around min size
            2 => RandomBuffer(rng, rng.Next(15, 350)),               // typical AX.25 KISS payload
            3 => RandomBuffer(rng, rng.Next(350, 4096)),             // oversized — well beyond paclen
            4 => SameByteBuffer(rng, (byte)rng.Next(256)),           // all-same-byte (FEND, FESC, 0x00, …)
            5 => SlipPathological(rng),                              // KISS-aware: lots of FENDs / FESCs / dangling escapes
            _ => MostlyValidAx25(rng),                               // structured: looks like an AX.25 frame
        };
    }

    private static byte[] SameByteBuffer(Random rng, byte value)
    {
        int length = rng.Next(0, 1024);
        var buf = new byte[length];
        Array.Fill(buf, value);
        return buf;
    }

    private static byte[] SlipPathological(Random rng)
    {
        int length = rng.Next(0, 512);
        var buf = new byte[length];
        for (int i = 0; i < length; i++)
        {
            // Heavy bias toward FEND/FESC/Tfend/Tfesc to exercise the
            // KISS escape state machine. Random fills the gaps.
            buf[i] = rng.Next(4) switch
            {
                0 => 0xC0, // FEND
                1 => 0xDB, // FESC
                2 => (byte)(rng.Next(2) == 0 ? 0xDC : 0xDD), // Tfend / Tfesc
                _ => (byte)rng.Next(256),
            };
        }
        return buf;
    }

    private static byte[] RandomBuffer(Random rng, int length)
    {
        var buf = new byte[length];
        rng.NextBytes(buf);
        return buf;
    }

    /// <summary>
    /// Produce a buffer that looks like an AX.25 frame: two 7-byte address
    /// slots, optional digipeaters, then a control byte and tail. Several
    /// byte positions are deliberately random so the parser exercises its
    /// reject branches.
    /// </summary>
    private static byte[] MostlyValidAx25(Random rng)
    {
        int digiCount = rng.Next(0, 10);                            // 0..9 — one extra to exceed the §6.1 max
        int infoLen   = rng.Next(0, 256);
        int total     = (2 + digiCount) * 7 + 1 + 1 + infoLen;     // dest+src+digis + ctrl + pid + info
        var buf       = new byte[total];

        int off = 0;
        WriteAddrSlot(buf, ref off, rng, isLast: false);            // destination
        WriteAddrSlot(buf, ref off, rng, isLast: digiCount == 0);   // source
        for (int d = 0; d < digiCount; d++)
        {
            WriteAddrSlot(buf, ref off, rng, isLast: d == digiCount - 1);
        }
        buf[off++] = (byte)rng.Next(256);                            // control
        buf[off++] = (byte)rng.Next(256);                            // pid
        rng.NextBytes(buf.AsSpan(off));                              // info
        return buf;
    }

    private static void WriteAddrSlot(byte[] buf, ref int off, Random rng, bool isLast)
    {
        // 6 callsign bytes (high 7 bits = ASCII << 1; low bit must be 0) — sometimes
        // valid-looking, sometimes garbage to exercise the address validator.
        for (int i = 0; i < 6; i++)
        {
            if (rng.Next(4) == 0)
            {
                buf[off + i] = (byte)rng.Next(256);
            }
            else
            {
                // valid-looking: A..Z or 0..9 shifted left.
                char c = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 "[rng.Next(37)];
                buf[off + i] = (byte)(c << 1);
            }
        }
        // SSID octet: bit 0 = E-bit (extension), bit 7 = H-bit/CRH (per slot's role).
        byte ssidByte = (byte)((rng.Next(16) << 1) | 0x60); // reserved bits "11" by spec default
        if (isLast)
        {
            ssidByte |= 0x01;                                   // set E-bit on the last slot
        }
        if (rng.Next(8) == 0)
        {
            ssidByte = (byte)rng.Next(256);                     // 1-in-8 fully random SSID byte
        }
        buf[off + 6] = ssidByte;
        off += 7;
    }

    // ─── targets ─────────────────────────────────────────────────────

    private static void FuzzAx25Bytes(byte[] bytes)
    {
        _ = Ax25Frame.TryParse(bytes, out _);
    }

    /// <summary>
    /// Drive arbitrary bytes through a fresh <see cref="KissDecoder"/>. The
    /// decoder is the byte-stream parser entry point for the KISS framing
    /// layer — equivalent of <c>KissFrame.TryParse</c> for a stream-oriented
    /// protocol.
    /// </summary>
    private static void FuzzKissBytes(byte[] bytes)
    {
        var decoder = new KissDecoder();
        decoder.Push(bytes);
    }

    // ─── afl / libfuzzer entry points ─────────────────────────────────

    private static int RunAx25Fuzzer(string[] args)
    {
        // SharpFuzz harness — afl-fuzz feeds stdin or a path under
        // Fuzzer.OutOfProcess.Run; we just call TryParse with whatever we get.
        Fuzzer.OutOfProcess.Run(stream =>
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            _ = Ax25Frame.TryParse(ms.ToArray(), out _);
        });
        return 0;
    }

    private static int RunKissFuzzer(string[] args)
    {
        Fuzzer.OutOfProcess.Run(stream =>
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var decoder = new KissDecoder();
            decoder.Push(ms.ToArray());
        });
        return 0;
    }

    private sealed record Finding(byte[] Input, Exception Exception);

    private sealed class SmokeResult(string label, int iterations)
    {
        public string Label { get; } = label;
        public int Iterations { get; } = iterations;
        public List<Finding> Findings { get; } = [];
    }
}
