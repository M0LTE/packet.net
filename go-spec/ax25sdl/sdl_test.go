package ax25sdl

import "testing"

// TestStatePagesHaveTransitions asserts every generated state-machine
// page declared a non-empty Transitions slice. A page with no
// transitions is almost certainly a codegen bug — the validator
// rejects YAML pages with zero transitions before they reach the
// emitter.
func TestStatePagesHaveTransitions(t *testing.T) {
	pages := []StatePage{
		DataLinkAwaitingConnection,
		DataLinkAwaitingConnection22,
		DataLinkAwaitingRelease,
		DataLinkConnected,
		DataLinkDisconnected,
	}
	for _, p := range pages {
		if len(p.Transitions) == 0 {
			t.Errorf("%s/%s has no transitions", p.Machine, p.State)
		}
	}
}

// TestSubroutinesPageHasBodies asserts the figc4.7 page declared its
// thirteen subroutines.
func TestSubroutinesPageHasBodies(t *testing.T) {
	const expected = 13
	if got := len(DataLinkSubroutines.Subroutines); got != expected {
		t.Errorf("expected %d subroutines on figc4.7, got %d", expected, got)
	}
}

// TestActionKindStringRoundTrips asserts every ActionKind value
// produces a non-"unknown" String, catching a generated file that
// names an out-of-range kind constant.
func TestActionKindStringRoundTrips(t *testing.T) {
	for k := SignalUpper; k <= InternalOut; k++ {
		if k.String() == "unknown" {
			t.Errorf("ActionKind(%d) → unknown", k)
		}
	}
}
