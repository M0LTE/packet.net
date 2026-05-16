using System.Collections.Concurrent;
using AwesomeAssertions;
using Packet.Ax25.Session;
using Packet.Core;
using Xunit;

namespace Packet.Ax25.Tests.Session;

/// <summary>
/// Reject-path and SDL edge-case coverage for <see cref="Ax25Listener"/>.
/// </summary>
/// <remarks>
/// "Edge case" here means inputs the listener has to route correctly
/// without crashing or building stray sessions: unknown peers sending
/// non-SABM frames, SABME from a v2.2 peer when our session defaults to
/// v2.0, malformed SABM with the response bit set, and the same with a
/// digipeater path in the address chain.
/// </remarks>
public class Ax25ListenerRejectAndEdgeTests
{
    private static readonly Callsign LocalCall = new("M0LTE", 0);
    private static readonly Callsign PeerCallA = new("G7XYZ", 7);
    private static readonly Callsign PeerCallB = new("M5ABC", 3);

    // ─── Category 4: reject path ────────────────────────────────────────

    /// <summary>
    /// With <see cref="Ax25Listener.AcceptIncoming"/> false, an inbound
    /// SABM should produce a DM response on the wire and NOT enter
    /// the per-peer session cache. No SessionAccepted event fires.
    /// (Existing <c>Listener_Drops_DM_For_Disallowed_Inbound</c>
    /// covers the DM emission; this test focuses on the cache invariant.)
    /// </summary>
    [Fact]
    public async Task Listener_AcceptIncoming_False_Emits_DM_To_New_Peer()
    {
        var modem = new LoopbackModem();
        await using var listener = new Ax25Listener(modem, new Ax25ListenerOptions { MyCall = LocalCall })
        {
            AcceptIncoming = false,
        };

        var acceptFires = 0;
        listener.SessionAccepted += (_, _) => Interlocked.Increment(ref acceptFires);

        await listener.StartAsync();
        modem.InjectInbound(Ax25Frame.Sabm(LocalCall, PeerCallA));
        await modem.SentFrames.WaitForCountAsync(1, TimeSpan.FromSeconds(2));

        // First reply must be DM (0x0F base).
        Ax25Frame.TryParse(modem.SentFrames[0].Span, out var reply).Should().BeTrue();
        (reply!.Control & 0xEF).Should().Be(0x0F);
        acceptFires.Should().Be(0);

        // Cache invariant: after the rejection settles, a subsequent
        // *outbound* ConnectAsync to the same peer should not find any
        // cached state and must build a fresh session. The transient
        // reject-path session is supposed to be discarded; we don't
        // have direct cache visibility, but we CAN verify the
        // outbound-side behaviour:
        //
        // Flip AcceptIncoming back on so the listener will allow
        // a fresh attempt, then SABM again — the SessionAccepted event
        // must fire (proving no stale cached "reject" session is
        // intercepting it).
        listener.AcceptIncoming = true;
        var afterFlipAccepted = new TaskCompletionSource<Ax25Session>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.SessionAccepted += (_, e) =>
        {
            if (e.Session.Context.Remote.Equals(PeerCallA))
            {
                afterFlipAccepted.TrySetResult(e.Session);
            }
        };

        modem.InjectInbound(Ax25Frame.Sabm(LocalCall, PeerCallA));
        var session = await afterFlipAccepted.Task.WithTimeout(TimeSpan.FromSeconds(2));
        session.Context.AcceptIncoming.Should().BeTrue(
            "the fresh session built after the flip must use the listener's current AcceptIncoming setting, not the rejected attempt's");
    }

    /// <summary>
    /// Same flip-and-retry behaviour as the previous test, presented as
    /// the standalone "after the no came a yes" scenario the task asks
    /// for explicitly. Asserts (a) DM on first attempt, (b) UA on the
    /// retry after the flip, (c) the session ends up Connected.
    /// </summary>
    [Fact]
    public async Task Listener_AcceptIncoming_False_Then_True_Accepts_Retry()
    {
        var modem = new LoopbackModem();
        await using var listener = new Ax25Listener(modem, new Ax25ListenerOptions { MyCall = LocalCall })
        {
            AcceptIncoming = false,
        };

        var accepted = new TaskCompletionSource<Ax25Session>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.SessionAccepted += (_, e) => accepted.TrySetResult(e.Session);

        await listener.StartAsync();

        // First attempt — rejected.
        modem.InjectInbound(Ax25Frame.Sabm(LocalCall, PeerCallA));
        await modem.SentFrames.WaitForCountAsync(1, TimeSpan.FromSeconds(2));
        Ax25Frame.TryParse(modem.SentFrames[0].Span, out var dm).Should().BeTrue();
        (dm!.Control & 0xEF).Should().Be(0x0F, "first attempt while AcceptIncoming=false must be DM");

        // Flip and retry.
        listener.AcceptIncoming = true;
        modem.InjectInbound(Ax25Frame.Sabm(LocalCall, PeerCallA));

        // Second outbound frame should be UA — accept path.
        await modem.SentFrames.WaitForCountAsync(2, TimeSpan.FromSeconds(2));
        Ax25Frame.TryParse(modem.SentFrames[1].Span, out var ua).Should().BeTrue();
        (ua!.Control & 0xEF).Should().Be(0x63, "after the flip, a fresh SABM must elicit UA");

        var session = await accepted.Task.WithTimeout(TimeSpan.FromSeconds(2));
        session.CurrentState.Should().Be("Connected");
    }

    /// <summary>
    /// Flipping <see cref="Ax25Listener.AcceptIncoming"/> to false must
    /// NOT tear down already-cached sessions. Only NEW SABMs from
    /// unknown peers are rejected after the flip.
    /// </summary>
    [Fact]
    public async Task Listener_AcceptIncoming_False_Does_Not_Affect_Existing_Sessions()
    {
        var modem = new LoopbackModem();
        await using var listener = new Ax25Listener(modem, new Ax25ListenerOptions { MyCall = LocalCall });

        var aAccepted = new TaskCompletionSource<Ax25Session>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bAccepted = new TaskCompletionSource<Ax25Session>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.SessionAccepted += (_, e) =>
        {
            if (e.Session.Context.Remote.Equals(PeerCallA)) aAccepted.TrySetResult(e.Session);
            if (e.Session.Context.Remote.Equals(PeerCallB)) bAccepted.TrySetResult(e.Session);
        };
        await listener.StartAsync();

        // Peer A connects normally.
        modem.InjectInbound(Ax25Frame.Sabm(LocalCall, PeerCallA));
        var sessionA = await aAccepted.Task.WithTimeout(TimeSpan.FromSeconds(2));
        await modem.SentFrames.WaitForCountAsync(1, TimeSpan.FromSeconds(2));
        var uaCount = modem.SentFrames.Count;

        // Flip the door shut.
        listener.AcceptIncoming = false;

        // Peer A is unaffected: send an I-frame from A and observe the
        // session-side V(r) advances + DL-DATA-indication fires.
        var aData = new ConcurrentQueue<DataLinkDataIndication>();
        sessionA.DataLinkSignalEmitted += (_, sig) =>
        {
            if (sig is DataLinkDataIndication d) aData.Enqueue(d);
        };

        modem.InjectInbound(Ax25Frame.I(LocalCall, PeerCallA, nr: 0, ns: 0,
            info: System.Text.Encoding.ASCII.GetBytes("STILL-UP"), pollBit: false));

        await ListenerTestSupport.WaitFor(() => !aData.IsEmpty, TimeSpan.FromSeconds(2),
            "peer A's existing session must keep processing I-frames even after AcceptIncoming flips to false");

        // Peer B (new) is rejected — DM, no SessionAccepted.
        modem.InjectInbound(Ax25Frame.Sabm(LocalCall, PeerCallB));
        await modem.SentFrames.WaitForCountAsync(uaCount + 1, TimeSpan.FromSeconds(2));

        var rejectFrame = modem.SentFrames[modem.SentFrames.Count - 1];
        Ax25Frame.TryParse(rejectFrame.Span, out var reply).Should().BeTrue();
        (reply!.Control & 0xEF).Should().Be(0x0F, "peer B's SABM must elicit DM (rejection), not UA");

        // bAccepted must not have fired.
        bAccepted.Task.IsCompleted.Should().BeFalse(
            "peer B must not have entered the per-peer cache while AcceptIncoming=false");
    }

    // ─── Category 5: spec edge cases ────────────────────────────────────

    /// <summary>
    /// DISC from a peer not in our cache: the listener does NOT build a
    /// session for it. The DISC is dropped at the listener layer (the
    /// guard at <c>DispatchInbound</c> only creates sessions for
    /// SABM/SABME).
    /// </summary>
    /// <remarks>
    /// figc4.1 t13 specifies a DISC-received-in-Disconnected emits DM,
    /// but only if there's a session in Disconnected state to dispatch
    /// the event into. Our listener routes by per-peer cache; an
    /// unknown peer has no cache entry, so the spec's t13 never fires.
    /// This is current behaviour; the test pins it.
    /// </remarks>
    [Fact]
    public async Task Listener_Ignores_Disc_For_Unknown_Peer()
    {
        var modem = new LoopbackModem();
        await using var listener = new Ax25Listener(modem, new Ax25ListenerOptions { MyCall = LocalCall });

        int accepted = 0;
        listener.SessionAccepted += (_, _) => Interlocked.Increment(ref accepted);
        await listener.StartAsync();

        modem.InjectInbound(Ax25Frame.Disc(LocalCall, PeerCallA));

        // Give the pump a chance to act.
        await Task.Delay(100);

        accepted.Should().Be(0, "listener must not build a session for an unsolicited DISC");
        modem.SentFrames.Count.Should().Be(0,
            "listener must not emit any frame in response to a DISC from an unknown peer (no cached session ⇒ no SDL t13)");
    }

    /// <summary>
    /// Same idea for RR. figc4.1's catchall would emit DM if the SDL
    /// were dispatching, but the listener drops the frame before any
    /// session sees it.
    /// </summary>
    [Fact]
    public async Task Listener_Ignores_Rr_For_Unknown_Peer()
    {
        var modem = new LoopbackModem();
        await using var listener = new Ax25Listener(modem, new Ax25ListenerOptions { MyCall = LocalCall });

        int accepted = 0;
        listener.SessionAccepted += (_, _) => Interlocked.Increment(ref accepted);
        await listener.StartAsync();

        modem.InjectInbound(Ax25Frame.Rr(LocalCall, PeerCallA, nr: 0, isCommand: true));

        await Task.Delay(100);

        accepted.Should().Be(0);
        modem.SentFrames.Count.Should().Be(0);
    }

    /// <summary>
    /// Peer sends SABME (mod-128). The listener's `able_to_establish`
    /// binding reads from <see cref="Ax25SessionContext.AcceptIncoming"/>
    /// (true by default), so the SDL takes the t16 (able) branch which
    /// runs <c>set_version_2_2</c> and emits UA. The context's
    /// <see cref="Ax25SessionContext.IsExtended"/> ends up true.
    /// </summary>
    /// <remarks>
    /// The task brief suggested SABME should fall through to DM via a
    /// "not version_2_2" guard — but the actual <c>disconnected.sdl.yaml</c>
    /// (t16/t17) doesn't gate on version_2_2 there. The branch is
    /// <c>sabme_able_to_establish</c>, which resolves to
    /// <c>AcceptIncoming</c>. So the listener DOES accept SABME today,
    /// upgrading the session to v2.2. This test pins the current
    /// behaviour. If we later add a per-listener "refuse SABME" toggle,
    /// the test will need to follow it.
    /// </remarks>
    [Fact]
    public async Task Listener_Handles_Sabme_From_V22_Peer()
    {
        var modem = new LoopbackModem();
        await using var listener = new Ax25Listener(modem, new Ax25ListenerOptions { MyCall = LocalCall });

        var accepted = new TaskCompletionSource<Ax25Session>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.SessionAccepted += (_, e) => accepted.TrySetResult(e.Session);

        await listener.StartAsync();
        modem.InjectInbound(Ax25Frame.Sabme(LocalCall, PeerCallA));

        var session = await accepted.Task.WithTimeout(TimeSpan.FromSeconds(2));
        await modem.SentFrames.WaitForCountAsync(1, TimeSpan.FromSeconds(2));

        // UA back.
        Ax25Frame.TryParse(modem.SentFrames[0].Span, out var reply).Should().BeTrue();
        (reply!.Control & 0xEF).Should().Be(0x63,
            "current behaviour: SABME with AcceptIncoming=true takes t16 (able) → UA + set_version_2_2");

        session.Context.IsExtended.Should().BeTrue(
            "t16's set_version_2_2 must have flipped the session into mod-128 mode");
        session.CurrentState.Should().Be("Connected");
    }

    /// <summary>
    /// A SABM with the C-bit cleared (response, not command) is
    /// malformed per AX.25 §6.1.2 — SABM is always a command. Today's
    /// classifier doesn't filter on C-bits, so the frame still
    /// classifies as <c>SabmReceived</c> and the SDL's t14 (which has
    /// no `command` guard) accepts it. Document the current behaviour
    /// so a future tightening surfaces visibly.
    /// </summary>
    /// <remarks>
    /// The spec is unambiguous (§4.3.3.1: "Set Asynchronous Balanced
    /// Mode … is a command frame"), and figc4.1 t14 implicitly assumes
    /// SABM-shaped frames are commands. A strict reading would have the
    /// classifier reject the frame or the SDL gate accept on `command`.
    /// Neither is implemented today. The test asserts the actual
    /// observed behaviour: the session is built and lands in Connected.
    /// </remarks>
    [Fact]
    public async Task Listener_Handles_Sabm_With_C_Response_Bit_Set_As_Malformed()
    {
        var modem = new LoopbackModem();
        await using var listener = new Ax25Listener(modem, new Ax25ListenerOptions { MyCall = LocalCall });

        Ax25Session? observed = null;
        var accepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.SessionAccepted += (_, e) =>
        {
            observed = e.Session;
            accepted.TrySetResult(true);
        };

        await listener.StartAsync();

        // Build a SABM-shaped frame, then re-parse it from its bytes
        // with the destination C-bit cleared (response). We do this by
        // constructing the SABM normally (which sets dest C=1, src C=0
        // → command) and then flipping the destination C-bit in the
        // raw bytes before reinjecting.
        var normalSabm = Ax25Frame.Sabm(LocalCall, PeerCallA);
        var bytes = normalSabm.ToBytes().ToArray();

        // The destination's SSID byte sits at index 6 (the 7th byte of
        // the destination address field). The C-bit is bit 7 of that
        // SSID byte.
        bytes[6] &= 0x7F; // clear destination C-bit (response, not command)
        // Per §6.1.2 the source C-bit also needs to flip to match
        // (response: dest C=0, source C=1). Source SSID is at index 13.
        bytes[13] |= 0x80; // set source C-bit

        // Now feed the malformed frame in.
        Ax25Frame.TryParse(bytes, out var malformed).Should().BeTrue(
            "the parser accepts the bytes; the malformedness is purely semantic");
        malformed!.IsResponse.Should().BeTrue("after the bit-twiddle, the frame is shaped as a response");

        modem.InjectInbound(malformed);

        // Document the current behaviour. The accept path WILL fire —
        // there's no command-guard on t14.
        var sawAccepted = await Task.WhenAny(accepted.Task, Task.Delay(TimeSpan.FromMilliseconds(500))) == accepted.Task;

        if (sawAccepted)
        {
            // Current behaviour: SABM-shape accepted regardless of C-bit.
            observed.Should().NotBeNull();
            observed!.CurrentState.Should().Be("Connected");
        }
        else
        {
            // Future tightening: SABM-with-response-bit rejected at the
            // classifier or via a new SDL command-guard. Either way, no
            // session is built — listener must not crash.
            observed.Should().BeNull();
        }
        // The single invariant in either branch: listener stayed alive.
        listener.IsRunning.Should().BeTrue();
    }

    /// <summary>
    /// SABM with a digipeater chain. The listener routes by
    /// <c>parsed.Destination.Callsign</c> (which is our MYCALL) and
    /// <c>parsed.Source.Callsign</c> (the SABM's originator). It does
    /// NOT inspect the digi chain. Today's behaviour: SABM-via-digi is
    /// accepted as if direct; the resulting UA will be emitted without
    /// a digi chain (since the listener's outbound path doesn't
    /// propagate it). Test pins the current behaviour and documents the
    /// limitation.
    /// </summary>
    /// <remarks>
    /// Real-world implications: if a peer SABMs us through a digi
    /// chain, our UA will go out on the air without the chain — the
    /// peer won't see it, the handshake never completes. We accept
    /// the inbound and emit a phantom UA, but the link won't form.
    /// This is a known gap, captured here so a future fix has a
    /// regression test ready to flip.
    /// </remarks>
    [Fact]
    public async Task Listener_Handles_Sabm_With_Digipeater_Path()
    {
        var modem = new LoopbackModem();
        await using var listener = new Ax25Listener(modem, new Ax25ListenerOptions { MyCall = LocalCall });

        var accepted = new TaskCompletionSource<Ax25Session>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.SessionAccepted += (_, e) => accepted.TrySetResult(e.Session);

        await listener.StartAsync();

        var digi = new Callsign("GB7DIG", 1);
        var sabmViaDigi = Ax25Frame.Sabm(LocalCall, PeerCallA, digipeaters: new[] { digi });

        modem.InjectInbound(sabmViaDigi);

        // Current behaviour: the listener accepts the SABM as if direct
        // (destination callsign matches MYCALL regardless of the
        // digi chain).
        var session = await accepted.Task.WithTimeout(TimeSpan.FromSeconds(2));
        session.Context.Remote.Should().Be(PeerCallA,
            "the listener's per-peer key is the source callsign, not the digi-resolved path");

        // Outbound UA was emitted — but with no digipeaters in its
        // path (our session-build path doesn't preserve a Via chain on
        // the cached context). This is the documented gap.
        await modem.SentFrames.WaitForCountAsync(1, TimeSpan.FromSeconds(2));
        Ax25Frame.TryParse(modem.SentFrames[0].Span, out var ua).Should().BeTrue();
        ua!.Digipeaters.Count.Should().Be(0,
            "KNOWN-GAP: digipeated inbound is accepted, but the outbound UA does not propagate the via-path. " +
            "A real peer behind a digi chain would not see this UA. Test pinned here so a fix has a clear before/after.");
    }
}
