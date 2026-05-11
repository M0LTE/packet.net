namespace Packet.Kiss;

/// <summary>
/// Stateful KISS frame decoder. Push bytes as they arrive; pull completed
/// frames out. Maintains the in-progress frame buffer + escape state across
/// calls so callers can push arbitrarily small chunks.
/// </summary>
public sealed class KissDecoder
{
    private readonly List<byte> currentFrame = new(256);
    private bool inEscape;

    /// <summary>
    /// Push a chunk of received bytes through the decoder. Each completed
    /// KISS frame (anything between two FENDs that isn't empty) is added to
    /// the returned list. Empty frames (FEND FEND) are silently dropped, as
    /// KISS implementations commonly use leading FENDs as a re-sync prefix.
    /// </summary>
    public IReadOnlyList<KissFrame> Push(ReadOnlySpan<byte> bytes)
    {
        var frames = new List<KissFrame>();
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];

            if (inEscape)
            {
                inEscape = false;
                switch (b)
                {
                    case KissFraming.Tfend:
                        currentFrame.Add(KissFraming.Fend);
                        break;
                    case KissFraming.Tfesc:
                        currentFrame.Add(KissFraming.Fesc);
                        break;
                    default:
                        // Per spec: receivers should be lenient with malformed
                        // escape sequences. Drop the byte and continue.
                        break;
                }
                continue;
            }

            switch (b)
            {
                case KissFraming.Fend:
                    if (currentFrame.Count > 0)
                    {
                        if (TryFinish(out var frame))
                        {
                            frames.Add(frame);
                        }
                        currentFrame.Clear();
                    }
                    // else: empty inter-frame FEND, ignore
                    break;
                case KissFraming.Fesc:
                    inEscape = true;
                    break;
                default:
                    currentFrame.Add(b);
                    break;
            }
        }
        return frames;
    }

    /// <summary>Discard any partially-decoded frame state.</summary>
    public void Reset()
    {
        currentFrame.Clear();
        inEscape = false;
    }

    private bool TryFinish(out KissFrame frame)
    {
        // Spec requires at least a command byte. Anything shorter is
        // framing garbage — drop it.
        if (currentFrame.Count < 1)
        {
            frame = default;
            return false;
        }

        byte commandByte = currentFrame[0];
        byte port = (byte)((commandByte >> 4) & 0x0F);
        var command = (KissCommand)(commandByte & 0x0F);
        var payload = currentFrame.Skip(1).ToArray();
        frame = new KissFrame(port, command, payload);
        return true;
    }
}
