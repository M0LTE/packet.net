"""SDL runtime types — hand-written. The .g.py files import from here.

Mirrors C#'s Packet.Ax25.Sdl types and ts-spec/types.ts. Empty-string
sentinel for absent strings; 0 for absent line numbers. Consumers
should treat empty values as absence.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum


class ActionKind(str, Enum):
    """Classifies how an SDL action verb interacts with the surrounding system.

    Mirrors the C# Packet.Ax25.Sdl.ActionKind enum and the kind groups
    in spec-sdl/actions.yaml.
    """

    SIGNAL_UPPER = "signal_upper"
    SIGNAL_LOWER = "signal_lower"
    PROCESSING = "processing"
    SUBROUTINE = "subroutine"
    INTERNAL_OUT = "internal_out"


@dataclass(frozen=True, slots=True)
class SdlSource:
    """Identifies which figure of which specification a page was transcribed from."""

    spec: str
    figure: str
    url: str  # "" = no URL recorded


@dataclass(frozen=True, slots=True)
class ActionStep:
    """One verb + kind pair along a transition or subroutine path.

    The verb is the canonical spelling from spec-sdl/actions.yaml;
    aliases are normalised at codegen time.
    """

    verb: str
    kind: ActionKind


@dataclass(frozen=True, slots=True)
class LoopRange:
    """A loop_while construct as a slice over the flat actions list.

    start/length describe the body; predicate is the boolean expression
    gating re-execution.
    """

    start: int
    length: int
    predicate: str


@dataclass(frozen=True, slots=True)
class ImplementationReference:
    """One citation supporting a transition or subroutine path.

    `source` is "spec_prose" or the key of a pinned_refs entry.
    Spec-prose citations populate cite/quote; code citations populate
    path/function/line.
    """

    source: str
    cite: str
    quote: str
    path: str
    function: str
    line: int  # 0 = no line citation
    note: str


@dataclass(frozen=True, slots=True)
class TransitionSpec:
    """One SDL transition column on a state-machine page.

    `from_` is the originating state; the trailing underscore avoids
    Python's `from` reserved word.
    """

    id: str
    from_: str
    on: str
    guard: str  # "" = unguarded
    actions: tuple[ActionStep, ...]
    next: str
    notes: str
    references: tuple[ImplementationReference, ...]
    loops: tuple[LoopRange, ...]


@dataclass(frozen=True, slots=True)
class SubroutinePath:
    """One path through a subroutine.

    Unlike a TransitionSpec there is no incoming event or destination
    state.
    """

    id: str
    guard: str
    actions: tuple[ActionStep, ...]
    notes: str
    references: tuple[ImplementationReference, ...]
    loops: tuple[LoopRange, ...]


@dataclass(frozen=True, slots=True)
class SubroutineSpec:
    """One subroutine on a subroutine page."""

    name: str
    paths: tuple[SubroutinePath, ...]
    notes: str
    references: tuple[ImplementationReference, ...]


@dataclass(frozen=True, slots=True)
class StatePage:
    """One generated state-machine page (figc4.1 / 4.2 / 4.3 / 4.4 / 4.6 etc.)."""

    machine: str
    state: str
    source: SdlSource
    transitions: tuple[TransitionSpec, ...]


@dataclass(frozen=True, slots=True)
class SubroutinesPage:
    """One generated subroutine page (figc4.7)."""

    machine: str
    source: SdlSource
    subroutines: tuple[SubroutineSpec, ...]
