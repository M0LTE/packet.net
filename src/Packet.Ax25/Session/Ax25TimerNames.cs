namespace Packet.Ax25.Session;

/// <summary>
/// The canonical scheduler keys for the AX.25 timer set, shared by every
/// component that arms, cancels, queries, or routes a timer through
/// <see cref="ITimerScheduler"/>.
/// </summary>
/// <remarks>
/// These names are a contract that spans files: <see cref="ActionDispatcher"/>
/// arms/cancels them, <see cref="Ax25Listener"/> (and <see cref="Ax25Adapter"/>)
/// route their expiries back into the SDL, and the #385 delayed-ack mechanism
/// depends on <c>ClearAcknowledgePending</c>'s <see cref="ITimerScheduler.Cancel"/>
/// naming the <em>same</em> T2 the listener's link-multiplexer arms. They were
/// previously bare string literals coordinated only by prose comments, where a
/// typo or rename on one side would silently break delayed-ack with no compile
/// error. Centralising them here removes that whole bug class.
/// </remarks>
internal static class Ax25TimerNames
{
    /// <summary>The outstanding-frame / link-establishment retry timer (AX.25 v2.2 §6.7.1).</summary>
    public const string T1 = "T1";

    /// <summary>The response-delay timer that gates delayed acknowledgement (AX.25 v2.2 §6.7.1.1).</summary>
    public const string T2 = "T2";

    /// <summary>The idle / keep-alive link timer (AX.25 v2.2 §6.7.1.3).</summary>
    public const string T3 = "T3";

    /// <summary>The management-data-link (XID/TEST) timer (AX.25 v2.2 §6.3.1).</summary>
    public const string TM201 = "TM201";
}
