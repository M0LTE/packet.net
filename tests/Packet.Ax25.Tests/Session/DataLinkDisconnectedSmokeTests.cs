using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Packet.Ax25.Sdl;
using Packet.Ax25.Session;
using Packet.Core;

namespace Packet.Ax25.Tests.Session;

/// <summary>
/// End-to-end smoke test: drives the codegen-emitted
/// <see cref="DataLink_Disconnected"/> transition table through
/// <see cref="Ax25Session"/> with a recording dispatcher and stubbed
/// decision predicates, then asserts that each of the 17 transitions
/// in figc4.1 lands on its declared next state with its declared
/// action sequence executed in order.
/// </summary>
/// <remarks>
/// What this catches:
/// <list type="bullet">
///   <item>Orchestrator routing — wrong transition picked for an event.</item>
///   <item>Guard parsing — compound guard expressions like
///   <c>"own_receiver_busy and not T1_running"</c> mis-evaluated.</item>
///   <item>Action dispatch order — actions executed in the wrong order
///   or with the wrong kind metadata.</item>
///   <item>State transition — <see cref="Ax25Session.CurrentState"/>
///   not updated to <c>Next</c>.</item>
/// </list>
/// What it doesn't catch: whether the YAML transcription correctly
/// reflects figc4.1 (still requires human review).
/// </remarks>
public class DataLinkDisconnectedSmokeTests
{
    private sealed class RecordingActionDispatcher : IActionDispatcher
    {
        public List<(string Verb, ActionKind Kind)> Recorded { get; } = new();

        public void Execute(IEnumerable<ActionStep> actions, TransitionContext tx)
        {
            foreach (var step in actions)
            {
                Recorded.Add((step.Verb, step.Kind));
            }
        }
    }

    private static (Ax25Session session, RecordingActionDispatcher recorder, GuardEvaluator guards) NewSession(
        bool pEq1 = false,
        bool ableToEstablish = false)
    {
        var time = new FakeTimeProvider();
        var scheduler = new SystemTimerScheduler(time);
        var ctx = new Ax25SessionContext
        {
            Local  = new Callsign("M0LTE", 0),
            Remote = new Callsign("G7XYZ", 7),
        };
        var bindings = new Dictionary<string, Func<bool>>(Ax25SessionBindings.CreateDefault(ctx, scheduler), StringComparer.Ordinal)
        {
            ["P_eq_1"]            = () => pEq1,
            ["able_to_establish"] = () => ableToEstablish,
        };
        var guards = new GuardEvaluator(bindings);
        var recorder = new RecordingActionDispatcher();
        var session = new Ax25Session(
            ctx, scheduler, recorder, guards,
            transitionsByState: new Dictionary<string, IReadOnlyList<TransitionSpec>>
            {
                ["Disconnected"]        = DataLink_Disconnected.Transitions,
                ["AwaitingConnection"]  = DataLink_AwaitingConnection.Transitions,
                ["Connected"]           = DataLink_Connected.Transitions,
            },
            initialState: "Disconnected");
        return (session, recorder, guards);
    }

    /// <summary>Stand-in frame for received-frame events; orchestrator routes by event name, not by frame contents.</summary>
    private static Ax25Frame Frame() => Ax25Frame.Ui(
        destination: new Callsign("M0LTE", 0),
        source:      new Callsign("G7XYZ", 7),
        info:        "x"u8);

    /// <summary>
    /// Asserts: after posting <paramref name="evt"/>, session lands on the
    /// declared next-state of <paramref name="transitionId"/>, and the
    /// recorded actions equal the declared action sequence exactly.
    /// </summary>
    private static void AssertTransitionFires(Ax25Event evt,
        bool pEq1 = false,
        bool ableToEstablish = false)
    {
        var (s, r, guards) = NewSession(pEq1: pEq1, ableToEstablish: ableToEstablish);
        
        var matching = DataLink_Disconnected.Transitions
            .Where(x => x.On == evt.Name)
            .Where(x => guards.Evaluate(x.Guard))
            .ToList();
        matching.Should().ContainSingle($"event '{evt.Name}' with the supplied guards should match exactly one transition in DataLink_Disconnected");
        var expected = matching[0];

        s.PostEvent(evt);

        s.CurrentState.Should().Be(expected.Next, $"transition '{expected.Id}' should land on '{expected.Next}'");
        r.Recorded.Should().Equal(
            expected.Actions.Select(a => (a.Verb, a.Kind)).ToArray(),
            $"transition '{expected.Id}' actions should fire in order");
    }

    // ─── Tests, one per transition (17 total) ──────────────────────────

    [Fact] public void t01_dl_disconnect_request() =>
        AssertTransitionFires(new DlDisconnectRequest());

    [Fact] public void t02_dl_unit_data_request() =>
        AssertTransitionFires(new DlUnitDataRequest("x"u8.ToArray()));

    [Fact] public void t03_dl_connect_request() =>
        AssertTransitionFires(new DlConnectRequest());

    [Fact] public void t04_all_other_primitives_from_lower_layer() =>
        AssertTransitionFires(new AllOtherPrimitivesFromLowerLayer());

    [Fact] public void t05_all_other_commands() =>
        AssertTransitionFires(new AllOtherCommands(Frame()));

    [Fact] public void t06_all_other_primitives_from_upper_layer() =>
        AssertTransitionFires(new AllOtherPrimitivesFromUpperLayer());

    [Fact] public void t07_control_field_error() =>
        AssertTransitionFires(new ControlFieldError());

    [Fact] public void t08_info_not_permitted_in_frame() =>
        AssertTransitionFires(new InfoNotPermittedInFrame());

    [Fact] public void t09_u_or_s_frame_length_error() =>
        AssertTransitionFires(new UOrSFrameLengthError());

    [Fact] public void t10_ua_received() =>
        AssertTransitionFires(new UaReceived(Frame()));

    [Fact] public void t11_ui_received_p_eq_1() =>
        AssertTransitionFires(new UiReceived(Frame()), pEq1: true);

    [Fact] public void t12_ui_received_p_eq_0() =>
        AssertTransitionFires(new UiReceived(Frame()), pEq1: false);

    [Fact] public void t13_disc_received() =>
        AssertTransitionFires(new DiscReceived(Frame()));

    [Fact] public void t14_sabm_received_able() =>
        AssertTransitionFires(new SabmReceived(Frame()), ableToEstablish: true);

    [Fact] public void t15_sabm_received_unable() =>
        AssertTransitionFires(new SabmReceived(Frame()), ableToEstablish: false);

    [Fact] public void t16_sabme_received_able() =>
        AssertTransitionFires(new SabmeReceived(Frame()), ableToEstablish: true);

    [Fact] public void t17_sabme_received_unable() =>
        AssertTransitionFires(new SabmeReceived(Frame()), ableToEstablish: false);
}
