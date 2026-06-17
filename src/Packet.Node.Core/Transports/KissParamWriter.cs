using Packet.Kiss;

namespace Packet.Node.Core.Transports;

/// <summary>
/// The write path that pushes a single KISS CSMA parameter to a live modem,
/// shared by the MCP <c>set_kiss_param</c> tool (and any other operate-side
/// caller). It owns the <b>settable-param set</b> and the per-parameter
/// validation, then dispatches to the matching <see cref="IKissModem"/>
/// setter so the value reaches the wire (KISS TXDELAY/PERSIST/SLOTTIME/TXTAIL
/// command frames on a serial or kiss-tcp link).
/// </summary>
/// <remarks>
/// <para>
/// The settable set is intentionally the four standard KISS CSMA parameters
/// the <see cref="IKissModem"/> surface exposes — <c>txdelay</c>,
/// <c>persist</c>, <c>slottime</c>, <c>txtail</c>. These take effect on the
/// live KISS link with no port restart: the TNC applies the value to every
/// subsequent transmission. Construction-time settings (e.g. ACKMODE, the
/// transport kind) are deliberately <em>not</em> here — they need a port
/// restart and a different (config) path.
/// </para>
/// <para>
/// All four are single-byte KISS parameters, so the valid range is 0..255.
/// Out-of-range or unrecognised inputs are rejected with a clear message
/// rather than silently clamped or dropped — a caller getting a
/// success-shaped response while nothing changed is the failure mode #466
/// set out to kill.
/// </para>
/// <para>
/// Some transports accept-and-ignore these knobs because they have no CSMA
/// channel (e.g. the AXUDP tunnel). That is an honest property of the
/// transport, not a failure of this writer: the write is dispatched the same
/// way, and the modem decides whether it is meaningful on its medium.
/// </para>
/// </remarks>
public static class KissParamWriter
{
    /// <summary>The canonical, case-insensitive settable-param names.</summary>
    public static readonly IReadOnlyList<string> SettableParams =
        ["txdelay", "persist", "slottime", "txtail"];

    /// <summary>The outcome of a KISS-parameter write attempt.</summary>
    /// <param name="Accepted">True when the value validated and was dispatched to the modem.</param>
    /// <param name="RequiresRestart">
    /// Whether the change needs a port restart to take effect. Always false for the
    /// live CSMA parameters this writer handles — they apply on the next transmission.
    /// </param>
    /// <param name="Message">A human-readable result/error message.</param>
    public sealed record Result(bool Accepted, bool RequiresRestart, string Message);

    /// <summary>
    /// Validate <paramref name="param"/>/<paramref name="value"/> and, if valid,
    /// push it to <paramref name="modem"/>. Returns a <see cref="Result"/> describing
    /// the outcome; never throws for bad input (those become rejecting results).
    /// </summary>
    public static async Task<Result> ApplyAsync(
        IKissModem modem,
        string param,
        int value,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(modem);

        if (string.IsNullOrWhiteSpace(param))
        {
            return Reject($"a parameter name is required (one of: {string.Join(", ", SettableParams)}).");
        }

        string normalised = param.Trim().ToLowerInvariant();

        // All four standard KISS CSMA params are single bytes.
        if (value is < 0 or > 255)
        {
            return Reject($"value {value} is out of range for '{normalised}' (must be 0..255).");
        }
        var b = (byte)value;

        switch (normalised)
        {
            case "txdelay":
                await modem.SetTxDelayAsync(b, ct).ConfigureAwait(false);
                return Applied("txdelay", value, "10 ms units");
            case "persist":
                await modem.SetPersistenceAsync(b, ct).ConfigureAwait(false);
                return Applied("persist", value, "p-persistence 0..255");
            case "slottime":
                await modem.SetSlotTimeAsync(b, ct).ConfigureAwait(false);
                return Applied("slottime", value, "10 ms units");
            case "txtail":
                await modem.SetTxTailAsync(b, ct).ConfigureAwait(false);
                return Applied("txtail", value, "10 ms units");
            default:
                return Reject(
                    $"unknown KISS parameter '{param}'. Settable: {string.Join(", ", SettableParams)}.");
        }
    }

    private static Result Applied(string name, int value, string units) =>
        new(Accepted: true, RequiresRestart: false,
            Message: $"KISS {name} set to {value} ({units}) on the modem.");

    private static Result Reject(string message) =>
        new(Accepted: false, RequiresRestart: false, Message: message);
}
