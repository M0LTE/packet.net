namespace Packet.Kiss;

/// <summary>
/// The surface area an adaptive controller / session-layer caller needs
/// from any KISS-speaking modem. Not NinoTNC-specific — works for any
/// modem that implements standard KISS plus the G8BPQ ACKMODE extension
/// (NinoTNC, QtSoundModem, Dire Wolf, etc.). The
/// <see cref="AdaptiveKissTransport"/> depends only on this interface so
/// alternate modems and fake-in-tests fixtures plug in cleanly.
/// </summary>
/// <remarks>
/// Mode-switching (KISS SETHW, byte semantics) is intentionally not on
/// this interface — it varies between modems. NinoTNC has a known mode
/// table and `+16` non-persist offset; Dire Wolf does not respond to
/// SETHW at all; QtSoundModem has its own scheme. Mode-aware helpers
/// live in modem-specific packages
/// (e.g. <c>Packet.Kiss.NinoTnc.NinoTncSerialPort.SetModeAsync</c>).
/// </remarks>
public interface IKissModem
{
    /// <summary>Send a KISS Data frame, fire-and-forget at this layer.</summary>
    Task SendFrameAsync(ReadOnlyMemory<byte> ax25Bytes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a frame in ACKMODE and await the TNC's TX-completion echo.
    /// </summary>
    Task<AckModeReceipt> SendFrameWithAckAsync(
        ReadOnlyMemory<byte> ax25Bytes,
        TimeSpan? timeout = null,
        ushort? sequenceTag = null,
        CancellationToken cancellationToken = default);

    /// <summary>KISS TXDELAY (0x01), units of 10 ms.</summary>
    Task SetTxDelayAsync(byte tenMsUnits, CancellationToken cancellationToken = default);

    /// <summary>KISS PERSIST (0x02), 0..255.</summary>
    Task SetPersistenceAsync(byte value, CancellationToken cancellationToken = default);

    /// <summary>KISS SLOTTIME (0x03), units of 10 ms.</summary>
    Task SetSlotTimeAsync(byte tenMsUnits, CancellationToken cancellationToken = default);

    /// <summary>
    /// KISS TXTAIL (0x04), units of 10 ms. Modern modems generally ignore
    /// this; the KISS TNC spec recommends 0. We expose a helper so the
    /// adaptive layer can drive it on experimental setups that care.
    /// </summary>
    Task SetTxTailAsync(byte tenMsUnits, CancellationToken cancellationToken = default);
}
