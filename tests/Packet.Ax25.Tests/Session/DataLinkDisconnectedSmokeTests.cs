using Packet.Ax25.Sdl;

namespace Packet.Ax25.Tests.Session;

/// <summary>
/// End-to-end smoke test for figc4.1 (Disconnected): drives every committed
/// transition in <see cref="DataLink_Disconnected"/> through
/// <see cref="Ax25Session"/> with a recording dispatcher and stubbed decision
/// predicates, asserting each lands on its declared next state with its declared
/// action sequence in order. Data-driven off the live table via
/// <see cref="DataLinkSmokeHarness"/>, so a transition added or renamed upstream
/// auto-appears here rather than slipping past a hand-maintained list.
/// </summary>
/// <remarks>
/// Catches orchestrator routing (wrong transition for an event), guard parsing
/// (compound <c>and</c>/<c>not</c> mis-evaluated), action dispatch order/kind, and
/// next-state. Does not catch whether the YAML transcription faithfully reflects
/// figc4.1 (human review) nor that the actions mutate live context correctly (the
/// behavioural conformance suite).
/// </remarks>
public class DataLinkDisconnectedSmokeTests
{
    public static IEnumerable<object[]> AllTransitions() =>
        DataLink_Disconnected.Transitions.Select(t => new object[] { t.Id });

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public void Transition_Fires_As_Declared(string transitionId) =>
        DataLinkSmokeHarness.AssertTransitionFiresAsDeclared(
            DataLink_Disconnected.Transitions, "Disconnected", transitionId);
}
