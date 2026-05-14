using System.Text;

namespace Packet.AprsIs.Spike;

/// <summary>
/// Parses a single line of TNC2-monitor format, the text representation
/// APRS-IS streams over the wire.
/// </summary>
/// <remarks>
/// Format: <c>SOURCE>DEST[,VIA1,VIA2,…]:payload</c>
///
/// Examples:
/// <code>
/// M0LTE-9>APRS,WIDE1-1,WIDE2-1:!5132.45N/00012.34W>Testing APRS
/// G0XYZ>APN384,TCPIP*,qAS,G0XYZ-2:&gt;Status text
/// </code>
///
/// Notes:
/// - Digipeater entries can end with <c>*</c> to indicate "frame has passed
///   through this digi" (the H-bit is set). We strip the <c>*</c> when
///   parsing the callsign but record the bit.
/// - APRS-IS injects <c>qAR</c>/<c>qAS</c>/<c>qAo</c>/<c>qAC</c> Q-construct
///   pseudo-digipeaters before the gateway callsign. These are NOT real
///   on-air digipeater hops — they're routing metadata. We preserve them
///   in the digipeater list so the consumer can decide to filter them
///   out.
/// - Tactical callsigns (e.g. <c>WX1ABC</c>, <c>WIDE1-1</c>) generally fit
///   the AX.25 6-char alphanumeric rule, but some real frames have weirder
///   addresses (long bases, lowercase, punctuation). Those will fail
///   <see cref="Packet.Core.Callsign.TryParse(string?, out Packet.Core.Callsign)"/>
///   downstream — by design, since they're the interesting failures.
/// </remarks>
public static class Tnc2Parser
{
    /// <summary>
    /// A structured parse of a TNC2 monitor line. <see cref="Source"/> and
    /// <see cref="Destination"/> are required; <see cref="Digipeaters"/> may
    /// be empty; <see cref="Info"/> may be empty (some frames carry no payload).
    /// </summary>
    public sealed record Tnc2Line(
        string Source,
        string Destination,
        IReadOnlyList<DigipeaterEntry> Digipeaters,
        ReadOnlyMemory<byte> Info,
        string Raw);

    /// <summary>
    /// A digipeater entry from the VIA path. <see cref="HasBeenRepeated"/>
    /// is true when the original text had a trailing <c>*</c> (the AX.25
    /// "H-bit set" indicator).
    /// </summary>
    public sealed record DigipeaterEntry(string Callsign, bool HasBeenRepeated);

    /// <summary>
    /// Try-parse a TNC2 line. Returns false if the line doesn't have the
    /// minimum required <c>SOURCE>DEST:payload</c> structure.
    /// </summary>
    public static bool TryParse(string line, out Tnc2Line parsed)
    {
        parsed = null!;
        if (string.IsNullOrEmpty(line)) return false;

        // Header lines from APRS-IS start with '#' — comments / server keepalives.
        if (line[0] == '#') return false;

        int gt = line.IndexOf('>', StringComparison.Ordinal);
        if (gt <= 0) return false;

        int colon = line.IndexOf(':', gt + 1);
        if (colon < 0) return false;

        string source = line[..gt];
        string addressTail = line[(gt + 1)..colon];
        string payload = line[(colon + 1)..];

        // addressTail is DEST[,VIA1,VIA2,...]
        var parts = addressTail.Split(',');
        if (parts.Length == 0) return false;

        string destination = parts[0];

        var digipeaters = new List<DigipeaterEntry>(parts.Length - 1);
        for (int i = 1; i < parts.Length; i++)
        {
            string entry = parts[i];
            if (entry.Length == 0) continue;
            bool hasBeenRepeated = entry[^1] == '*';
            string call = hasBeenRepeated ? entry[..^1] : entry;
            digipeaters.Add(new DigipeaterEntry(call, hasBeenRepeated));
        }

        // The payload is text in APRS-IS but may contain 8-bit bytes
        // (mic-E compressed positions, weather bursts). Encode as ISO-8859-1
        // to preserve all bytes losslessly; APRS-IS itself is line-based and
        // shouldn't include CR/LF in a payload, so the raw payload string
        // is the byte sequence verbatim.
        byte[] infoBytes = Encoding.Latin1.GetBytes(payload);

        parsed = new Tnc2Line(source, destination, digipeaters, infoBytes, line);
        return true;
    }

    /// <summary>
    /// Distinguish APRS-IS Q-construct pseudo-digipeaters (<c>qAR</c>,
    /// <c>qAS</c>, <c>qAo</c>, <c>qAC</c>, …) from real digipeater hops.
    /// </summary>
    public static bool IsQConstruct(string call) =>
        call.Length >= 3 && call[0] == 'q' && char.IsUpper(call[1]);
}
