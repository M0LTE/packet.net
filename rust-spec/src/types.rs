//! SDL runtime types — hand-written.
//!
//! The `.g.rs` files in this crate reference these. Mirrors C#'s
//! `Packet.Ax25.Sdl` types and `go-spec/ax25sdl/types.go`.
//!
//! Empty-string and zero-int conventions are used in place of `Option`
//! throughout, to keep the generated initialisers readable. A `guard`
//! of `""` means "no guard"; a `line` of `0` in an
//! `ImplementationReference` means "no line citation"; etc.

/// Classifies how an SDL action verb interacts with the surrounding
/// system. Mirrors the C# `Packet.Ax25.Sdl.ActionKind` enum and the
/// kind groups in `spec-sdl/actions.yaml`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ActionKind {
    SignalUpper,
    SignalLower,
    Processing,
    Subroutine,
    InternalOut,
}

/// Identifies which figure of which specification a page was
/// transcribed from.
#[derive(Debug, Clone, Copy)]
pub struct SdlSource {
    pub spec: &'static str,
    pub figure: &'static str,
    /// Empty string = no URL recorded.
    pub url: &'static str,
}

/// One verb + kind pair along a transition or subroutine path. The
/// `verb` is the canonical spelling from `spec-sdl/actions.yaml`;
/// aliases are normalised at codegen time.
#[derive(Debug, Clone, Copy)]
pub struct ActionStep {
    pub verb: &'static str,
    pub kind: ActionKind,
}

/// Records a `loop_while` construct as a slice over the flat
/// `actions` list. `start` + `length` describe the body; `predicate`
/// is the boolean expression gating re-execution.
#[derive(Debug, Clone, Copy)]
pub struct LoopRange {
    pub start: usize,
    pub length: usize,
    pub predicate: &'static str,
}

/// Points at one citation supporting a transition or subroutine path.
/// `source` is `"spec_prose"` or the key of a `pinned_refs` entry.
/// Spec-prose citations populate `cite`/`quote`; code citations
/// populate `path`/`function`/`line`.
#[derive(Debug, Clone, Copy)]
pub struct ImplementationReference {
    pub source: &'static str,
    pub cite: &'static str,
    pub quote: &'static str,
    pub path: &'static str,
    pub function: &'static str,
    /// 0 = no line citation.
    pub line: u32,
    pub note: &'static str,
}

/// Describes one SDL transition column on a state-machine page.
#[derive(Debug, Clone, Copy)]
pub struct TransitionSpec {
    pub id: &'static str,
    pub from: &'static str,
    pub on: &'static str,
    /// Empty string = no guard.
    pub guard: &'static str,
    pub actions: &'static [ActionStep],
    pub next: &'static str,
    /// Empty string = no notes.
    pub notes: &'static str,
    pub references: &'static [ImplementationReference],
    pub loops: &'static [LoopRange],
}

/// Describes one path through a subroutine. Unlike a `TransitionSpec`
/// there is no incoming event or destination state.
#[derive(Debug, Clone, Copy)]
pub struct SubroutinePath {
    pub id: &'static str,
    pub guard: &'static str,
    pub actions: &'static [ActionStep],
    pub notes: &'static str,
    pub references: &'static [ImplementationReference],
    pub loops: &'static [LoopRange],
}

/// Describes one subroutine on a subroutine page.
#[derive(Debug, Clone, Copy)]
pub struct SubroutineSpec {
    pub name: &'static str,
    pub paths: &'static [SubroutinePath],
    pub notes: &'static str,
    pub references: &'static [ImplementationReference],
}

/// One generated state-machine page (figc4.1 / 4.2 / 4.3 / 4.4 /
/// 4.6 / etc.).
#[derive(Debug, Clone, Copy)]
pub struct StatePage {
    pub machine: &'static str,
    pub state: &'static str,
    pub source: SdlSource,
    pub transitions: &'static [TransitionSpec],
}

/// One generated subroutine page (figc4.7).
#[derive(Debug, Clone, Copy)]
pub struct SubroutinesPage {
    pub machine: &'static str,
    pub source: SdlSource,
    pub subroutines: &'static [SubroutineSpec],
}
