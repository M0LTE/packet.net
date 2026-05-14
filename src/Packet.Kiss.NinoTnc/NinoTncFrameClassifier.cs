using Packet.Kiss;

namespace Packet.Kiss.NinoTnc;

/// <summary>
/// NinoTNC overlay over <see cref="KissFrameClassifier"/>. Runs the
/// generic classification first and then upgrades the result when the
/// frame matches a NinoTNC-firmware-specific shape — specifically the
/// synthetic TX-Test diagnostic frame the firmware emits when the
/// front-panel button is pressed.
/// </summary>
public static class NinoTncFrameClassifier
{
    /// <summary>
    /// Classify <paramref name="frame"/> with NinoTNC firmware awareness.
    /// Returns one of: <see cref="NinoTncTxTestFrameReceivedEvent"/>,
    /// <see cref="Ax25FrameReceivedEvent"/>,
    /// <see cref="AckModeDataReceivedEvent"/>, or
    /// <see cref="UnknownInboundEvent"/>. Never null.
    /// </summary>
    public static KissInboundEvent Classify(KissFrame frame)
    {
        var generic = KissFrameClassifier.Classify(frame);

        // 1) Synthetic host-side TX-Test diagnostic — the KISS Data frame
        //    the firmware sends to its own host when its button is pressed.
        //    The "=FirmwareVr:" ASCII marker is the authoritative signal.
        if (generic is Ax25FrameReceivedEvent or UnknownInboundEvent &&
            frame.Command == KissCommand.Data &&
            NinoTncTxTestFrame.TryParse(frame, out var diag) && diag is not null)
        {
            return new NinoTncTxTestFrameReceivedEvent(frame, diag);
        }

        // 2) Over-air TX-Test UI frame — the AX.25 frame the *other*
        //    NinoTNC's modulator put on the air when its button was
        //    pressed. We receive this through our own modem as a normal
        //    KISS Data frame; the generic classifier already gave us the
        //    parsed Ax25Frame.
        if (generic is Ax25FrameReceivedEvent ax25Evt &&
            NinoTncAirTestFrame.TryRecognise(ax25Evt.Ax25, out var air) && air is not null)
        {
            return new NinoTncAirTestFrameReceivedEvent(frame, air);
        }

        return generic;
    }
}
