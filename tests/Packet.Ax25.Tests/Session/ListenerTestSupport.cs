using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Packet.Kiss;

namespace Packet.Ax25.Tests.Session;

/// <summary>
/// Shared test helpers for <c>Ax25Listener</c> unit tests. The
/// <see cref="LoopbackModem"/> in particular is used by every
/// <c>Ax25Listener*Tests.cs</c> file — extracted here so additions in one
/// file (e.g. a new injector overload) automatically benefit the others.
/// </summary>
/// <remarks>
/// Keep this internal to the test assembly. The Listener API doesn't
/// expose any kind of fake-modem fixture and shouldn't — the listener's
/// dependency on <see cref="IKissModem"/> is the seam tests poke through.
/// </remarks>
internal static class ListenerTestSupport
{
    /// <summary>
    /// Block until <paramref name="condition"/> returns <c>true</c>, polling
    /// every 20 ms. Throws <see cref="TimeoutException"/> if the deadline
    /// is reached first.
    /// </summary>
    public static async Task WaitFor(Func<bool> condition, TimeSpan budget, string? reason = null)
    {
        var deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(20);
        }
        throw new TimeoutException($"condition did not become true within {budget}{(reason is null ? "" : $" — {reason}")}");
    }
}

/// <summary>
/// In-memory <see cref="IKissModem"/> whose inbound stream is a channel
/// the test writes <see cref="KissFrame"/>s into. Outbound
/// <c>SendFrameAsync</c> appends to <see cref="SentFrames"/> for the test
/// to assert against.
/// </summary>
/// <remarks>
/// Extended over the original (private nested in <c>Ax25ListenerTests</c>)
/// with these utilities the broader coverage tests need:
/// <list type="bullet">
/// <item><see cref="DropOutbound"/> — if <c>true</c>, outbound frames are
/// counted but not appended to <see cref="SentFrames"/>. Lets tests
/// simulate "peer never saw our UA" without changing the listener API.</item>
/// <item><see cref="SendDelay"/> — sleep this long inside
/// <see cref="SendFrameAsync"/> before recording. Lets tests probe whether
/// the listener's outbound path is on a separate thread from the inbound
/// pump (it should be — fire-and-forget).</item>
/// <item><see cref="OutboundFrameCount"/> — count of every send attempt
/// regardless of <see cref="DropOutbound"/>.</item>
/// </list>
/// </remarks>
internal sealed class LoopbackModem : IKissModem
{
    private readonly Channel<KissFrame> rx = Channel.CreateUnbounded<KissFrame>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    /// <summary>Outbound frames the listener / session has handed to the modem.</summary>
    public ObservableList<ReadOnlyMemory<byte>> SentFrames { get; } = new();

    /// <summary>If <c>true</c>, outbound frames are dropped — <see cref="SentFrames"/> stays empty.</summary>
    public bool DropOutbound { get; set; }

    /// <summary>If non-zero, <see cref="SendFrameAsync"/> sleeps for this long before recording.</summary>
    public TimeSpan SendDelay { get; set; } = TimeSpan.Zero;

    private int outboundCount;

    /// <summary>Total send attempts regardless of <see cref="DropOutbound"/>.</summary>
    public int OutboundFrameCount => Volatile.Read(ref outboundCount);

    /// <summary>Push an inbound AX.25 frame as if it had been heard off the air.</summary>
    public void InjectInbound(Ax25Frame frame)
    {
        var kf = new KissFrame((byte)0, KissCommand.Data, frame.ToBytes().ToArray());
        rx.Writer.TryWrite(kf);
    }

    /// <summary>
    /// Push an inbound raw KISS frame. Useful for non-Data commands the
    /// listener should skip (TX echoes etc.).
    /// </summary>
    public void InjectInboundRaw(KissFrame frame)
    {
        rx.Writer.TryWrite(frame);
    }

    public async Task SendFrameAsync(ReadOnlyMemory<byte> ax25Bytes, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref outboundCount);
        if (SendDelay > TimeSpan.Zero)
        {
            await Task.Delay(SendDelay, cancellationToken).ConfigureAwait(false);
        }
        if (!DropOutbound)
        {
            SentFrames.Add(ax25Bytes);
        }
    }

    public async IAsyncEnumerable<KissFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await rx.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (rx.Reader.TryRead(out var f))
            {
                yield return f;
            }
        }
    }

    /// <summary>
    /// When non-null, <see cref="SendFrameWithAckAsync"/> is supported: the frame
    /// is recorded like a plain send and the call's receipt resolves when the test
    /// releases this gate (one release = one TX-completion echo) — letting tests
    /// control exactly when a frame "clears the air". When null (default), ACKMODE
    /// is unsupported and the call throws, like a plain non-ACKMODE modem.
    /// </summary>
    public SemaphoreSlim? AckEchoGate { get; set; }

    public async Task<AckModeReceipt> SendFrameWithAckAsync(
        ReadOnlyMemory<byte> ax25Bytes, TimeSpan? timeout = null, ushort? sequenceTag = null,
        CancellationToken cancellationToken = default)
    {
        if (AckEchoGate is not { } gate)
        {
            throw new NotSupportedException("this LoopbackModem has no ACKMODE (AckEchoGate not set).");
        }
        var queued = DateTimeOffset.UtcNow;
        Interlocked.Increment(ref outboundCount);
        if (!DropOutbound)
        {
            SentFrames.Add(ax25Bytes.ToArray());
        }
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new AckModeReceipt(sequenceTag ?? 0, queued, DateTimeOffset.UtcNow);
    }
    public Task SetTxDelayAsync(byte v, CancellationToken c = default) => Task.CompletedTask;
    public Task SetPersistenceAsync(byte v, CancellationToken c = default) => Task.CompletedTask;
    public Task SetSlotTimeAsync(byte v, CancellationToken c = default) => Task.CompletedTask;
    public Task SetTxTailAsync(byte v, CancellationToken c = default) => Task.CompletedTask;
}

/// <summary>
/// Tiny thread-safe list with a "wait until count reaches N" helper.
/// Lets tests block deterministically on the modem's outbound queue
/// without polling sleeps littered through the assertions.
/// </summary>
internal sealed class ObservableList<T>
{
    private readonly List<T> items = new();
    private readonly object gate = new();
    private readonly List<TaskCompletionSource<bool>> waiters = new();

    public void Add(T item)
    {
        List<TaskCompletionSource<bool>> toComplete;
        lock (gate)
        {
            items.Add(item);
            toComplete = waiters.ToList();
            waiters.Clear();
        }
        foreach (var w in toComplete) w.TrySetResult(true);
    }

    public int Count
    {
        get { lock (gate) return items.Count; }
    }

    public T this[int i]
    {
        get { lock (gate) return items[i]; }
    }

    public List<T> SnapshotList()
    {
        lock (gate) return items.ToList();
    }

    public async Task WaitForCountAsync(int target, TimeSpan budget)
    {
        var deadline = DateTimeOffset.UtcNow + budget;
        while (true)
        {
            Task wait;
            lock (gate)
            {
                if (items.Count >= target) return;
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                waiters.Add(tcs);
                wait = tcs.Task;
            }
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException($"only {Count}/{target} items after {budget}");
            var done = await Task.WhenAny(wait, Task.Delay(remaining));
            if (done != wait) throw new TimeoutException($"only {Count}/{target} items after {budget}");
        }
    }
}

internal static class ListenerTestTaskExtensions
{
    public static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan budget)
    {
        var done = await Task.WhenAny(task, Task.Delay(budget));
        if (done != task) throw new TimeoutException($"task did not complete within {budget}");
        return await task;
    }

    public static async Task WithTimeout(this Task task, TimeSpan budget)
    {
        var done = await Task.WhenAny(task, Task.Delay(budget));
        if (done != task) throw new TimeoutException($"task did not complete within {budget}");
        await task;
    }
}
