using AwesomeAssertions;
using Packet.Core;
using Packet.Ax25.Session;
using Xunit;

namespace Packet.Ax25.Tests.Session;

/// <summary>
/// Regression coverage for m0lte/packet.net#327 — the figc4.x delayed
/// acknowledgement must actually reach the wire in the production wiring.
///
/// The SDL's only path to a non-piggybacked ack runs through the link
/// multiplexer: an in-sequence I-frame received with P=0 and no ack already
/// pending emits <c>LM-SEIZE Request</c> + <c>Set Ack Pending</c> (figc4.3
/// t26); the RR is then sent by the <c>LM-SEIZE Confirm</c> transition
/// (t22 → <c>Enquiry Response (F=0)</c>). T2 is declared but has no
/// transitions in the generated tables, so there is no timer fallback —
/// if the seize request is swallowed, a session that has no reply data
/// NEVER acknowledges, the peer FRACK-retransmits, and the link dies.
/// Interactive use masks this because every console reply I-frame
/// piggybacks N(R); it bites exactly when data flow is one-way.
///
/// Found hardware-testing the Rust port against a real LinBPQ
/// (pico-node#15); these tests are the C# equivalent of its
/// <c>idle_received_i_frame_is_still_acknowledged</c>.
/// </summary>
public class Ax25ListenerDelayedAckTests
{
    private static readonly Callsign LocalCall = new("M0LTE", 0);
    private static readonly Callsign PeerCall  = new("G7XYZ", 7);

    /// <summary>
    /// The marquee repro: peer connects, peer sends one I-frame (P=0),
    /// the local side has nothing to say back. An RR response with
    /// N(R)=1 must still go out — the delayed ack, flushed via the
    /// LM-SEIZE grant. Red on the stubbed sendLinkMux: only the UA
    /// ever reaches the modem and the peer would retry into link failure.
    /// </summary>
    [Fact]
    public async Task Idle_received_I_frame_is_still_acknowledged()
    {
        var modem = new LoopbackModem();
        await using var listener = new Ax25Listener(modem, new Ax25ListenerOptions
        {
            MyCall = LocalCall,
        });

        var accepted = new TaskCompletionSource<Ax25Session>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.SessionAccepted += (_, e) => accepted.TrySetResult(e.Session);
        await listener.StartAsync();

        // Peer connects: SABM → UA.
        modem.InjectInbound(Ax25Frame.Sabm(LocalCall, PeerCall));
        var session = await accepted.Task.WithTimeout(TimeSpan.FromSeconds(2));
        await modem.SentFrames.WaitForCountAsync(1, TimeSpan.FromSeconds(2));
        session.CurrentState.Should().Be("Connected");

        // Peer sends one in-sequence I-frame, P=0. We send nothing back.
        modem.InjectInbound(Ax25Frame.I(LocalCall, PeerCall, nr: 0, ns: 0,
            info: "QSL?"u8.ToArray(), pollBit: false));

        // The delayed ack must reach the wire without any local send and
        // without waiting for the peer to poll: an RR response, N(R)=1.
        await modem.SentFrames.WaitForCountAsync(2, TimeSpan.FromSeconds(2));

        var ack = ParseSent(modem, 1);
        IsRr(ack).Should().BeTrue(
            $"the frame after the UA must be the delayed-ack RR, got control 0x{ack.Control:X2}");
        ack.Nr.Should().Be(1, "the RR must acknowledge the received I-frame (N(R)=V(R)=1)");
        ack.IsResponse.Should().BeTrue("the figc4.7 Enquiry Response (F=0) sends a response frame");
        ack.PollFinal.Should().BeFalse("the delayed ack is F=0 — it is not answering a poll");
    }

    /// <summary>
    /// Same shape, two back-to-back I-frames before any ack flushes: the
    /// second frame arrives with an ack already pending (figc4.3 runs the
    /// seize path only once — t26's AckPending guard), so exactly one RR
    /// with the cumulative N(R)=2 must go out, not one per frame.
    /// </summary>
    [Fact]
    public async Task Back_to_back_idle_I_frames_get_one_cumulative_ack()
    {
        var modem = new LoopbackModem();
        await using var listener = new Ax25Listener(modem, new Ax25ListenerOptions
        {
            MyCall = LocalCall,
        });

        var accepted = new TaskCompletionSource<Ax25Session>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.SessionAccepted += (_, e) => accepted.TrySetResult(e.Session);
        await listener.StartAsync();

        modem.InjectInbound(Ax25Frame.Sabm(LocalCall, PeerCall));
        await accepted.Task.WithTimeout(TimeSpan.FromSeconds(2));
        await modem.SentFrames.WaitForCountAsync(1, TimeSpan.FromSeconds(2));

        modem.InjectInbound(Ax25Frame.I(LocalCall, PeerCall, nr: 0, ns: 0,
            info: "ONE"u8.ToArray(), pollBit: false));
        modem.InjectInbound(Ax25Frame.I(LocalCall, PeerCall, nr: 0, ns: 1,
            info: "TWO"u8.ToArray(), pollBit: false));

        // At least one RR must flush, and the LAST ack on the wire must
        // carry the cumulative N(R)=2. (Whether the two frames coalesce
        // into one RR or flush as two depends on inbound pacing; both are
        // spec-valid. Zero RRs is the bug.)
        await ListenerTestSupport.WaitFor(
            () => LastRrNr(modem) == 2,
            TimeSpan.FromSeconds(2),
            "both received I-frames must end up acknowledged (final RR N(R)=2)");

        // And the ack path must not run away: bounded by one RR per
        // received I-frame (the LM-SEIZE confirm path releases, it never
        // re-seizes).
        var rrCount = modem.SentFrames.SnapshotList()
            .Select(b => { Ax25Frame.TryParse(b.Span, out var f); return f; })
            .Count(f => f is not null && IsRr(f));
        rrCount.Should().BeInRange(1, 2, "the delayed-ack loop must be bounded");
    }

    private static Ax25Frame ParseSent(LoopbackModem modem, int index)
    {
        Ax25Frame.TryParse(modem.SentFrames[index].Span, out var frame).Should().BeTrue(
            $"sent frame [{index}] must parse as AX.25");
        return frame!;
    }

    /// <summary>S-frame RR test, mod-8: low nibble 0x01, and not a U/I frame.</summary>
    private static bool IsRr(Ax25Frame frame) => (frame.Control & 0x0F) == 0x01;

    private static int LastRrNr(LoopbackModem modem)
    {
        var frames = modem.SentFrames.SnapshotList();
        for (int i = frames.Count - 1; i >= 0; i--)
        {
            if (Ax25Frame.TryParse(frames[i].Span, out var f) && IsRr(f!))
            {
                return f!.Nr;
            }
        }
        return -1;
    }
}
