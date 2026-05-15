"""Cross-page smoke tests. Hand-written, not regenerated.

Mirrors ts-spec/src/ax25sdl/sdl.test.ts: end-to-end checks that the
generated dataclass instances import cleanly and expose the structure
downstream consumers will rely on.
"""

from ax25sdl import (
    DATA_LINK_AWAITING_CONNECTION,
    DATA_LINK_AWAITING_RELEASE,
    DATA_LINK_AWAITING_CONNECTION_22,
    DATA_LINK_CONNECTED,
    DATA_LINK_DISCONNECTED,
    DATA_LINK_SUBROUTINES,
)


def test_state_pages_have_transitions() -> None:
    for page in (
        DATA_LINK_AWAITING_CONNECTION,
        DATA_LINK_AWAITING_RELEASE,
        DATA_LINK_AWAITING_CONNECTION_22,
        DATA_LINK_CONNECTED,
        DATA_LINK_DISCONNECTED,
    ):
        assert len(page.transitions) > 0, f"{page.state} has no transitions"


def test_state_pages_report_data_link_machine() -> None:
    for page in (
        DATA_LINK_AWAITING_CONNECTION,
        DATA_LINK_AWAITING_RELEASE,
        DATA_LINK_AWAITING_CONNECTION_22,
        DATA_LINK_CONNECTED,
        DATA_LINK_DISCONNECTED,
    ):
        assert page.machine == "data_link"


def test_subroutines_page_has_subroutines() -> None:
    assert len(DATA_LINK_SUBROUTINES.subroutines) > 0


def test_establish_data_link_present() -> None:
    names = [s.name for s in DATA_LINK_SUBROUTINES.subroutines]
    assert "Establish_Data_Link" in names
