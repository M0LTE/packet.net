namespace Packet.Node.Tests.Support;

/// <summary>
/// Real-time polling helper for the node integration tests.
/// </summary>
/// <remarks>
/// <para>
/// The AX.25 listener pump and the console run on their own background tasks
/// against <c>TimeProvider.System</c> — the pump is a real <c>Task.Run</c>
/// reading a channel, so it is <b>inherently real-time</b> and a
/// <c>FakeTimeProvider</c> cannot make its task continuations deterministic (it
/// only controls timer expiry, not thread-pool scheduling). These tests therefore
/// poll a condition with a timeout (mirroring the engine's own
/// <c>ListenerTestSupport.WaitFor</c>); the deterministic <c>FakeTimeProvider</c>
/// path is used by the config / reconcile-delta unit tests where the component
/// under test takes an injectable clock.
/// </para>
/// <para>
/// <b>Why the budget is generous and the poll is timer-driven (#47 flake fix).</b>
/// CI runs <c>ci.yml</c>'s test matrix as ~12 <c>dotnet test</c> processes in
/// parallel on a <em>single</em> self-hosted runner. When the CPU-heavy siblings
/// (the FsCheck property suites, the loss-recovery / conformance harness, interop)
/// saturate every core, this assembly's background pump/console continuations get
/// scheduling-starved. The work always completes <em>correctly</em> once a core is
/// free (the handshake over an in-memory channel is microseconds of actual work);
/// it was only ever <em>late</em>. The old helper used a 5 s wall-clock deadline
/// and an <c>await Task.Delay(20)</c> poll — but (a) 5 s is shorter than the
/// worst-case scheduling latency on a fully-saturated box, and (b) the
/// <c>Task.Delay</c> continuation competes for the same starved thread pool as the
/// work it is waiting on, so the <em>poller itself</em> was being starved. Two
/// changes make this deterministic-enough without weakening any assertion:
/// </para>
/// <list type="number">
/// <item>The default budget is raised to a generous-but-bounded 30 s — comfortably
/// over worst-case scheduling latency, while still failing a genuine hang (rather
/// than letting it run to the CI job timeout). A passing run is unaffected: the
/// condition is observed the instant the work is scheduled, so fast stays fast.</item>
/// <item>The poll is driven by a <see cref="PeriodicTimer"/> (timer-queue fired,
/// not a thread-pool continuation chain), so the checker keeps ticking and
/// re-evaluates the condition promptly even while the pump tasks are contending
/// for CPU.</item>
/// </list>
/// <para>
/// The dominant CI symptom — a ~66 s job — was the engine's
/// <c>Ax25Listener.ConnectAsync</c> burning its full <c>(N2+1)·T1V</c> budget
/// (11 × 6 s with the spec defaults) when the peer's UA was starved past it. The
/// test stations (<see cref="RemoteStation"/>, <see cref="EchoStation"/>) and the
/// node ports under test now use short test T1/N2 values so that budget is
/// seconds, not 66 s — the in-memory channel is instant, so a real connect never
/// needs the long backstop, and a genuine failure fails fast.
/// </para>
/// </remarks>
public static class Wait
{
    /// <summary>The default wait budget. Generous (covers worst-case scheduling
    /// latency on a saturated CI runner) but bounded (a genuine hang still fails
    /// the test instead of hanging the job).</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(30);

    public static Task ForAsync(Func<bool> condition, string because) =>
        ForAsync(condition, because, DefaultBudget);

    public static async Task ForAsync(Func<bool> condition, string because, TimeSpan budget)
    {
        // Fast path: already satisfied.
        if (condition())
        {
            return;
        }

        var deadline = DateTimeOffset.UtcNow + budget;
        // Timer-queue-driven poll: the tick fires independently of thread-pool
        // saturation, so the poller is not starved by the very contention it is
        // waiting out (see the class remarks). 15 ms keeps a passing run snappy.
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(15));
        while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            if (condition())
            {
                return;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException($"condition not met within {budget.TotalSeconds:0.#}s: {because}");
            }
        }
    }
}
