using Packet.Ax25.Sdl;

namespace Packet.Ax25.Tests.Session;

/// <summary>
/// End-to-end smoke test for figc4.2 (Awaiting Connection): drives every
/// committed transition in <see cref="DataLink_AwaitingConnection"/> through
/// <see cref="Ax25Session"/> with a recording dispatcher and stubbed decision
/// predicates, asserting each lands on its declared next state with its declared
/// action sequence in order. Data-driven off the live table via
/// <see cref="DataLinkSmokeHarness"/>, so a transition added or renamed upstream
/// auto-appears here rather than slipping past a hand-maintained list.
/// </summary>
public class DataLinkAwaitingConnectionSmokeTests
{
    public static IEnumerable<object[]> AllTransitions() =>
        DataLink_AwaitingConnection.Transitions.Select(t => new object[] { t.Id });

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public void Transition_Fires_As_Declared(string transitionId) =>
        DataLinkSmokeHarness.AssertTransitionFiresAsDeclared(
            DataLink_AwaitingConnection.Transitions, "AwaitingConnection", transitionId);
}
