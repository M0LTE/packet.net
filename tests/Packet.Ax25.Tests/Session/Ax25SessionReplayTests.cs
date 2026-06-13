using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Packet.Ax25.Sdl;
using Packet.Ax25.Session;
using Packet.Core;

namespace Packet.Ax25.Tests.Session;

/// <summary>
/// <see cref="Ax25Session.AttachConsumerWithReplay"/>: a consumer that attaches AFTER the session
/// has already emitted inbound DL-DATA (the outbound-connect case — the peer's connect banner
/// arrives before the wrapping <c>Ax25NodeConnection</c> can subscribe) still receives that early
/// data, exactly once, ahead of the live stream. This is the unit-level guard for the
/// banner-lost-on-connect regression that the AXUDP node-to-node test proves end-to-end.
/// </summary>
public sealed class Ax25SessionReplayTests
{
    // A no-op dispatcher: these tests only exercise RaiseDataLinkSignal / AttachConsumerWithReplay,
    // neither of which drives a transition, so the action dispatcher is never invoked.
    private sealed class NoOpDispatcher : IActionDispatcher
    {
        public void Execute(IEnumerable<ActionStep> actions, TransitionContext tx) { }
    }

    private static Ax25Session NewSession()
    {
        var scheduler = new SystemTimerScheduler(new FakeTimeProvider());
        var ctx = new Ax25SessionContext { Local = new Callsign("M0LTE", 0), Remote = new Callsign("G7XYZ", 7) };
        var guards = new GuardEvaluator(Ax25SessionBindings.CreateDefault(ctx, scheduler));
        return new Ax25Session(
            ctx, scheduler, new NoOpDispatcher(), guards,
            transitionsByState: new Dictionary<string, IReadOnlyList<TransitionSpec>>
            {
                ["Disconnected"]          = DataLink_Disconnected.Transitions,
                ["AwaitingConnection"]    = DataLink_AwaitingConnection.Transitions,
                ["AwaitingV22Connection"] = DataLink_AwaitingV22Connection.Transitions,
                ["Connected"]             = DataLink_Connected.Transitions,
                ["AwaitingRelease"]       = DataLink_AwaitingRelease.Transitions,
                ["TimerRecovery"]         = DataLink_TimerRecovery.Transitions,
            },
            initialState: "Connected");
    }

    private static DataLinkDataIndication Data(string text) =>
        new(System.Text.Encoding.ASCII.GetBytes(text), Ax25Frame.PidNoLayer3);

    [Fact]
    public void Data_emitted_before_attach_is_replayed_to_the_late_consumer()
    {
        var session = NewSession();
        // Peer's "banner" lands before any consumer is attached.
        session.RaiseDataLinkSignal(Data("BANNER>"));

        var seen = new List<string>();
        session.AttachConsumerWithReplay((_, sig) =>
        {
            if (sig is DataLinkDataIndication di) seen.Add(System.Text.Encoding.ASCII.GetString(di.Info.Span));
        });

        seen.Should().ContainSingle().Which.Should().Be("BANNER>", "the pre-subscribe banner is replayed exactly once");
    }

    [Fact]
    public void Live_data_after_attach_flows_and_is_not_duplicated_with_the_replay()
    {
        var session = NewSession();
        session.RaiseDataLinkSignal(Data("EARLY"));

        var seen = new List<string>();
        session.AttachConsumerWithReplay((_, sig) =>
        {
            if (sig is DataLinkDataIndication di) seen.Add(System.Text.Encoding.ASCII.GetString(di.Info.Span));
        });

        // A signal emitted after the attach goes live, once.
        session.RaiseDataLinkSignal(Data("LIVE"));

        seen.Should().Equal("EARLY", "LIVE");
    }

    [Fact]
    public void Only_data_indications_are_buffered_not_other_signals()
    {
        var session = NewSession();
        // A non-data signal before attach must not be buffered/replayed (it isn't inbound data).
        session.RaiseDataLinkSignal(new DataLinkConnectConfirm());

        var seen = new List<DataLinkSignal>();
        session.AttachConsumerWithReplay((_, sig) => seen.Add(sig));

        seen.Should().BeEmpty("only DL-DATA indications are replayed, not connect/other signals");
    }
}
