using Packet.Ax25.Sdl;

namespace Packet.Ax25.Tests.Session;

/// <summary>
/// End-to-end smoke test for figc4.5 (Timer Recovery): drives every committed
/// transition in <see cref="DataLink_TimerRecovery"/> through
/// <see cref="Ax25Session"/> with a recording dispatcher and stubbed decision
/// predicates, asserting each lands on its declared next state with its declared
/// action sequence in order. Data-driven off the live table via
/// <see cref="DataLinkSmokeHarness"/>, so a transition added or renamed upstream
/// auto-appears here rather than slipping past a hand-maintained list.
/// </summary>
/// <remarks>
/// figc4.5 is the densest data-link page (90 transitions / 45 decisions). The
/// harness derives a satisfying assignment from each transition's own guard and
/// asserts a unique match, so a guard that isn't mutually exclusive with its
/// siblings surfaces as a collision. This is the page the data-driven pattern was
/// first written for, now shared by all six states via the harness.
/// </remarks>
public class DataLinkTimerRecoverySmokeTests
{
    public static IEnumerable<object[]> AllTransitions() =>
        DataLink_TimerRecovery.Transitions.Select(t => new object[] { t.Id });

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public void Transition_Fires_As_Declared(string transitionId) =>
        DataLinkSmokeHarness.AssertTransitionFiresAsDeclared(
            DataLink_TimerRecovery.Transitions, "TimerRecovery", transitionId);
}
