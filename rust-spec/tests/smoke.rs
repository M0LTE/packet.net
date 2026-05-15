//! Cross-page smoke tests. Asserts page-shape invariants that don't
//! belong inside any single `.g.rs` file's `mod tests` block. Each
//! per-page `.g.rs` already has its own granular `#[cfg(test)]` module
//! checking every transition's id/on/next/guard/actions; this file
//! just sanity-checks the pages are all present + non-empty.

use ax25sdl::*;

#[test]
fn state_pages_have_transitions() {
    let pages: &[(&str, &[TransitionSpec])] = &[
        ("data_link/Disconnected", DATA_LINK_DISCONNECTED.transitions),
        (
            "data_link/AwaitingConnection",
            DATA_LINK_AWAITING_CONNECTION.transitions,
        ),
        (
            "data_link/AwaitingConnection22",
            DATA_LINK_AWAITING_CONNECTION_22.transitions,
        ),
        (
            "data_link/AwaitingRelease",
            DATA_LINK_AWAITING_RELEASE.transitions,
        ),
        ("data_link/Connected", DATA_LINK_CONNECTED.transitions),
    ];
    for (label, transitions) in pages {
        assert!(!transitions.is_empty(), "{label} has no transitions");
    }
}

#[test]
fn subroutines_page_has_thirteen() {
    assert_eq!(
        DATA_LINK_SUBROUTINES.subroutines.len(),
        13,
        "figc4.7 should declare 13 subroutines"
    );
}

#[test]
fn subroutines_page_includes_establish_data_link() {
    let found = DATA_LINK_SUBROUTINES
        .subroutines
        .iter()
        .any(|s| s.name == "Establish_Data_Link");
    assert!(
        found,
        "Establish_Data_Link should be one of the 13 subroutines"
    );
}
