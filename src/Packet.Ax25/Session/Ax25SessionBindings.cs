namespace Packet.Ax25.Session;

/// <summary>
/// Helpers to wire the <see cref="GuardEvaluator"/> against an
/// <see cref="Ax25SessionContext"/> + <see cref="ITimerScheduler"/>.
/// </summary>
/// <remarks>
/// The bindings table maps each identifier the SDL's guard expressions
/// can reference to a closure that reads its current value. The
/// vocabulary grows as new transcriptions land — new identifiers get
/// added here, and the evaluator throws <see cref="GuardEvaluationException"/>
/// on unbound names so typos surface fast.
/// </remarks>
public static class Ax25SessionBindings
{
    /// <summary>
    /// Build the standard binding table for an AX.25 session — every
    /// identifier the SDL transcriptions reference, mapped to a closure
    /// over the supplied context and scheduler.
    /// </summary>
    public static IReadOnlyDictionary<string, Func<bool>> CreateDefault(
        Ax25SessionContext context,
        ITimerScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scheduler);

        return new Dictionary<string, Func<bool>>(StringComparer.Ordinal)
        {
            // ─── Flags (§C4.3) ──────────────────────────────────────────
            ["own_receiver_busy"]          = () => context.OwnReceiverBusy,
            ["peer_receiver_busy"]         = () => context.PeerReceiverBusy,
            ["acknowledge_pending"]        = () => context.AcknowledgePending,
            ["reject_exception"]           = () => context.RejectException,
            ["selective_reject_exception"] = () => context.SelectiveRejectException,
            ["layer_3_initiated"]          = () => context.Layer3Initiated,
            ["srej_enabled"]               = () => context.SrejEnabled,

            // ─── Timer state ───────────────────────────────────────────
            ["T1_running"]                 = () => scheduler.IsRunning("T1"),
            ["T2_running"]                 = () => scheduler.IsRunning("T2"),
            ["T3_running"]                 = () => scheduler.IsRunning("T3"),
        };
    }
}
