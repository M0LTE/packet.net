using Packet.Ax25;
using Packet.Core;

namespace Packet.AprsIs.Spike;

/// <summary>
/// Shared helpers for reconstructing an <see cref="Ax25Frame"/> from a
/// parsed TNC2 line and round-tripping it through the binary parser.
///
/// Lifted out of <see cref="OneshotMode"/> so both <c>oneshot</c> (live
/// stream) and <c>analyse</c> (offline corpus replay) can call the same
/// reconstruction pipeline. Single source of truth for what "AX.25
/// reconstructable" means.
/// </summary>
public static class FrameReconstruct
{
    public static bool TryReconstruct(Tnc2Parser.Tnc2Line parsed, out Ax25Frame frame, out string? error)
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

        // Q-construct entries (qAR/qAS/qAC/...) are APRS-IS routing metadata,
        // not real on-air hops. Stop at the first one and treat anything
        // earlier as the actual digipeater path.
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

    public static bool StructurallyEqual(Ax25Frame built, Ax25Frame decoded)
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

    /// <summary>
    /// Bucket a reconstruct error message into a small set of canonical kind
    /// strings, useful for histograms.
    /// </summary>
    public static string BucketReconstructError(string err) =>
        err.StartsWith("invalid source callsign", StringComparison.Ordinal)      ? "invalid_source" :
        err.StartsWith("invalid destination callsign", StringComparison.Ordinal) ? "invalid_destination" :
        err.StartsWith("invalid digipeater callsign", StringComparison.Ordinal)  ? "invalid_digipeater" :
        "other";
}
