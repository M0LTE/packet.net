using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Packet.Ax25.Sdl;
using Packet.Ax25.Session;
using Packet.Core;

namespace Packet.Ax25.Tests.Session;

/// <summary>
/// End-to-end smoke test for figc4.5 (Timer Recovery) — drives every
/// committed transition in <see cref="DataLink_TimerRecovery"/> through
/// <see cref="Ax25Session"/> with a recording dispatcher and stubbed
/// decision predicates, asserting each transition lands on its declared
/// next state with its declared action sequence.
/// </summary>
/// <remarks>
/// figc4.5 is the second-densest data-link page (90 transitions / 45
/// decisions). Rather than 90 hand-written facts, this test uses a
/// theory driver: for each transition, it auto-derives a Guards
/// configuration that satisfies the guard expression, posts the event,
/// and asserts the runtime lands on the declared <c>next</c> state with
/// the declared action sequence in order.
/// </remarks>
public class DataLinkTimerRecoverySmokeTests
{
    public static IEnumerable<object[]> AllTransitions() =>
        DataLink_TimerRecovery.Transitions.Select(t => new object[] { t.Id });

    /// <summary>
    /// Every atom mentioned in any guard on the page. The smoke test
    /// binds them all (default <c>false</c>) before applying the target
    /// transition's overrides, so the per-event uniqueness check
    /// (<see cref="Transition_Fires_As_Declared"/>) can evaluate sibling
    /// guards without hitting unbound-identifier errors.
    /// </summary>
    private static readonly IReadOnlyCollection<string> AllAtoms =
        DataLink_TimerRecovery.Transitions
            .SelectMany(t => GuardsThatSatisfy(t.Guard ?? "").Keys)
            .ToHashSet(StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public void Transition_Fires_As_Declared(string transitionId)
    {
        var transition = DataLink_TimerRecovery.Transitions.Single(t => t.Id == transitionId);
        var allFalse = AllAtoms.ToDictionary(a => a, _ => false, StringComparer.Ordinal);
        foreach (var (name, value) in GuardsThatSatisfy(transition.Guard ?? ""))
            allFalse[name] = value;
        var (session, recorder, guards) = NewSession(allFalse);

        var matching = DataLink_TimerRecovery.Transitions
            .Where(t => t.On == transition.On)
            .Where(t => guards.Evaluate(t.Guard))
            .ToList();
        matching.Should().ContainSingle(
            $"transition '{transitionId}' on event '{transition.On}' with derived guards must match exactly one transition (collisions imply the auto-derivation is ambiguous for this guard expression)");
        matching[0].Id.Should().Be(transitionId,
            "the matched transition should be the one we're targeting");

        session.PostEvent(EventFor(transition.On));

        session.CurrentState.Should().Be(transition.Next,
            $"transition '{transitionId}' should land on '{transition.Next}'");

        // Loops run against this harness's empty session state, so a head-test
        // (while) loop body executes zero times and is absent from the recorded
        // sequence; a tail-test (do-while) body runs once (= the flat list).
        // Loop iteration is covered behaviourally elsewhere, not in this smoke test.
        var headLoopBody = new HashSet<int>();
        foreach (var loop in transition.Loops.Where(l => !l.TestAtEnd))
            for (int i = loop.Start; i < loop.Start + loop.Length; i++)
                headLoopBody.Add(i);
        var expectedRecorded = transition.Actions
            .Where((_, i) => !headLoopBody.Contains(i))
            .Select(a => (a.Verb, a.Kind))
            .ToArray();

        recorder.Recorded.Should().Equal(expectedRecorded,
            $"transition '{transitionId}' actions should fire in declared order (head-test loop bodies run zero times against empty harness state)");
    }

    // ─── Guards parsing ────────────────────────────────────────────────

    /// <summary>
    /// Parse a guard expression into a `name → value` dictionary that
    /// satisfies the expression. The expression grammar is
    /// `atom [ (and|or) [not] atom ]*`. We treat <c>or</c> as <c>and</c>
    /// for derivation purposes (taking the first conjunctive branch),
    /// since figc4.5 only uses <c>and</c> across compound predicates.
    /// </summary>
    private static Dictionary<string, bool> GuardsThatSatisfy(string guard)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(guard)) return result;

        var tokens = guard.Split(new[] { " and " }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in tokens)
        {
            var t = raw.Trim();
            var negated = t.StartsWith("not ", StringComparison.Ordinal);
            var atom = negated ? t.Substring(4).Trim() : t;
            result[atom] = !negated;
        }
        return result;
    }

    // ─── Event factory ─────────────────────────────────────────────────

    private static Ax25Event EventFor(string onName) => onName switch
    {
        "DL_DISCONNECT_request"               => new DlDisconnectRequest(),
        "DL_CONNECT_request"                  => new DlConnectRequest(),
        "DL_DATA_request"                     => new DlDataRequest("x"u8.ToArray()),
        "DL_UNIT_DATA_request"                => new DlUnitDataRequest("x"u8.ToArray()),
        "DL_FLOW_OFF_request"                 => new DlFlowOffRequest(),
        "DL_FLOW_ON_request"                  => new DlFlowOnRequest(),
        "I_frame_pops_off_queue"              => new IFramePopsOffQueue("x"u8.ToArray()),
        "I_received"                          => new IFrameReceived(Frame()),
        "RR_received"                         => new RrReceived(Frame()),
        "RNR_received"                        => new RnrReceived(Frame()),
        "REJ_received"                        => new RejReceived(Frame()),
        "SREJ_received"                       => new SrejReceived(Frame()),
        "SABM_received"                       => new SabmReceived(Frame()),
        "SABME_received"                      => new SabmeReceived(Frame()),
        "DISC_received"                       => new DiscReceived(Frame()),
        "UA_received"                         => new UaReceived(Frame()),
        "DM_received"                         => new DmReceived(Frame()),
        "FRMR_received"                       => new FrmrReceived(Frame()),
        "UI_received"                         => new UiReceived(Frame()),
        "LM_SEIZE_confirm"                    => new LmSeizeConfirm(),
        "T1_expiry"                           => new T1Expiry(),
        "control_field_error"                 => new ControlFieldError(),
        "info_not_permitted_in_frame"         => new InfoNotPermittedInFrame(),
        "u_or_s_frame_length_error"           => new UOrSFrameLengthError(),
        "all_other_primitives__from_lower_layer" => new AllOtherPrimitivesFromLowerLayer(),
        "all_other_primitives__from_upper_layer" => new AllOtherPrimitivesFromUpperLayer(),
        _ => throw new InvalidOperationException($"no event factory for on='{onName}' (add a case)"),
    };

    private static Ax25Frame Frame() => Ax25Frame.Ui(
        destination: new Callsign("M0LTE", 0),
        source:      new Callsign("G7XYZ", 7),
        info:        "x"u8);

    // ─── Session + recorder ────────────────────────────────────────────

    private sealed class RecordingActionDispatcher : IActionDispatcher
    {
        public List<(string Verb, ActionKind Kind)> Recorded { get; } = new();

        public void Execute(IEnumerable<ActionStep> actions, TransitionContext tx)
        {
            foreach (var step in actions)
                Recorded.Add((step.Verb, step.Kind));
        }
    }

    private static (Ax25Session session, RecordingActionDispatcher recorder, GuardEvaluator guards) NewSession(
        Dictionary<string, bool> guardValues)
    {
        var time = new FakeTimeProvider();
        var scheduler = new SystemTimerScheduler(time);
        var ctx = new Ax25SessionContext
        {
            Local  = new Callsign("M0LTE", 0),
            Remote = new Callsign("G7XYZ", 7),
        };
        // Start from the runtime defaults so things like timer-state and
        // counter predicates have sensible bindings, then override with
        // the figc4.5 atoms named by this transition's guard.
        var bindings = new Dictionary<string, Func<bool>>(
            Ax25SessionBindings.CreateDefault(ctx, scheduler), StringComparer.Ordinal);
        foreach (var (name, value) in guardValues)
            bindings[name] = () => value;

        var guards = new GuardEvaluator(bindings);
        var recorder = new RecordingActionDispatcher();
        var session = new Ax25Session(
            ctx, scheduler, recorder, guards,
            transitionsByState: new Dictionary<string, IReadOnlyList<TransitionSpec>>
            {
                ["Disconnected"]          = DataLink_Disconnected.Transitions,
                ["AwaitingConnection"]    = DataLink_AwaitingConnection.Transitions,
                ["AwaitingV22Connection"] = DataLink_AwaitingV22Connection.Transitions,
                ["Connected"]             = DataLink_Connected.Transitions,
                ["AwaitingRelease"]       = DataLink_AwaitingRelease.Transitions,
                ["TimerRecovery"]         = DataLink_TimerRecovery.Transitions,
            },
            initialState: "TimerRecovery");
        return (session, recorder, guards);
    }
}
