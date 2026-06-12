using Packet.Kiss;

namespace Packet.LinkBench.Channel;

/// <summary>
/// Pass-through decorator that records every <see cref="AckModeReceipt"/> the
/// inner modem returns. Slots between <c>PacingKissModem</c> and the channel
/// endpoint so the bench can report ackmode echo round-trip stats — in
/// particular rung 2's open question (plan §7): is net-sim's 0x0C echo
/// immediate-on-receive (pacing is a no-op) or post-transmission (pacing real)?
/// Compare the recorded RTTs against the frame's modeled airtime.
/// </summary>
internal sealed class AckReceiptTap(IKissModem inner, Action<AckModeReceipt> onReceipt) : IKissModem, IAsyncDisposable
{
    public Task SendFrameAsync(ReadOnlyMemory<byte> ax25Bytes, CancellationToken cancellationToken = default) =>
        inner.SendFrameAsync(ax25Bytes, cancellationToken);

    public async Task<AckModeReceipt> SendFrameWithAckAsync(
        ReadOnlyMemory<byte> ax25Bytes, TimeSpan? timeout = null, ushort? sequenceTag = null,
        CancellationToken cancellationToken = default)
    {
        var receipt = await inner.SendFrameWithAckAsync(ax25Bytes, timeout, sequenceTag, cancellationToken)
            .ConfigureAwait(false);
        onReceipt(receipt);
        return receipt;
    }

    public IAsyncEnumerable<KissFrame> ReadFramesAsync(CancellationToken cancellationToken = default) =>
        inner.ReadFramesAsync(cancellationToken);

    public Task SetTxDelayAsync(byte tenMsUnits, CancellationToken cancellationToken = default) =>
        inner.SetTxDelayAsync(tenMsUnits, cancellationToken);

    public Task SetPersistenceAsync(byte value, CancellationToken cancellationToken = default) =>
        inner.SetPersistenceAsync(value, cancellationToken);

    public Task SetSlotTimeAsync(byte tenMsUnits, CancellationToken cancellationToken = default) =>
        inner.SetSlotTimeAsync(tenMsUnits, cancellationToken);

    public Task SetTxTailAsync(byte tenMsUnits, CancellationToken cancellationToken = default) =>
        inner.SetTxTailAsync(tenMsUnits, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        // Standard decorator inner-ownership chain (mirrors PacingKissModem).
        switch (inner)
        {
            case IAsyncDisposable ad:
                await ad.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable d:
                d.Dispose();
                break;
        }
    }
}
