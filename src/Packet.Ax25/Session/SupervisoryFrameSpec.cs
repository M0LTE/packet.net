namespace Packet.Ax25.Session;

/// <summary>
/// Identifies one of the four AX.25 supervisory (S) frame subtypes per §4.3.2.
/// </summary>
public enum SupervisoryFrameType
{
    /// <summary>Receive Ready — RR.</summary>
    Rr,
    /// <summary>Receive Not Ready — RNR.</summary>
    Rnr,
    /// <summary>Reject — REJ.</summary>
    Rej,
    /// <summary>Selective Reject — SREJ.</summary>
    Srej,
}

/// <summary>
/// A request to send a supervisory frame. The dispatcher emits these in
/// response to actions like <c>"RR command"</c> / <c>"RNR response"</c>;
/// the session translates them into <see cref="Ax25Frame"/>s and ships
/// them on the wire.
/// </summary>
/// <param name="Type">RR / RNR / REJ / SREJ.</param>
/// <param name="IsCommand">
/// <c>true</c> for a command frame (P bit may be set), <c>false</c> for a
/// response frame (F bit may be set). The action string spelled in the SDL
/// (<c>"RR command"</c> vs <c>"RR response"</c>) selects this.
/// </param>
public readonly record struct SupervisoryFrameSpec(SupervisoryFrameType Type, bool IsCommand);
