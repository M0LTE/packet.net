# ADR-0001 — AX.25 SDL state diagrams via a YAML DSL with codegen

- **Status:** Accepted
- **Date:** 2026-05-11
- **Deciders:** Tom Fanning M0LTE (initial author)

## Context

The AX.25 2.2 specification
(https://github.com/packethacking/ax25spec/blob/main/doc/ax.25.2.2.4_Oct_25.md)
defines the protocol's state machines via 27 SDL diagrams in its appendices.
The diagrams are bitmap images (PNG / JPEG); the spec text describes
procedures but does not include a machine-readable encoding of the SDLs.

We need to implement these state machines with high confidence that what we
ship matches the spec. Two failure modes are easy:

1. **Hand-coded state machine drifts from the SDLs.** A future contributor
   "fixes" what they perceive as a bug and silently diverges from the spec.
2. **Reviewers can't tell.** A reviewer cannot eyeball a few hundred lines of
   imperative C# against a screenshot and be sure transitions are faithful.

The dev team also includes one human + LLM agent collaborators. Both produce
better work when the spec is encoded in a form that lets each transition be
reviewed in isolation alongside the figure it came from.

## Decision

We introduce a small YAML DSL for SDL state machines, stored under
`/spec-sdl/`. A standalone console tool, `tools/Packet.Sdl.CodeGen`, reads the
DSL and emits:

- `src/Packet.Ax25.Sdl/*.g.cs` — partial classes containing the state machine
  transitions.
- `tests/Packet.Ax25.Conformance.Tests/*.g.Tests.cs` — one xUnit `[Fact]` per
  transition, asserting `(state, event, guard) → (next state, actions)`.

Both generated outputs are **checked in**. CI regenerates them on every build
and fails if `git diff --exit-code` reports differences.

The DSL schema lives at `/spec-sdl/schema/sdl-machine.schema.json`. A shared
event catalog lives at `/spec-sdl/events.yaml`.

### Worked example

```yaml
# /spec-sdl/data-link/connected.sdl.yaml
machine: data_link
state: Connected
source:
  spec: ax.25.2.2.4_Oct_25
  figure: figc4.4a
  url: https://raw.githubusercontent.com/packethacking/ax25spec/main/doc/media/figc4.4a.png
variables: [V(S), V(A), V(R), RC, srej_enabled]
transitions:
  - id: t01_dl_data_request
    on: DL_DATA_request
    guard: peer_busy == false
    actions:
      - push_iframe(V(S), info)
      - start_T1
      - "V(S) := V(S) + 1"
    next: Connected
```

## Alternatives considered

### 1. Hand-write the state machine in C#

Rejected. Cannot prove faithfulness to the spec. A reviewer would have to read
the C# alongside the figure and convince themselves transition-by-transition.
Easy to drift.

### 2. Roslyn source generator

Rejected. Source generators run at build time and don't produce files in the
working tree, so the generated transitions wouldn't appear in PR diffs.
Reviewers want to see the generated C# next to the YAML.

### 3. T4 templates

Rejected. Legacy tooling, poor diff UX, generates verbose output, and lacks
ergonomic support for the cross-validation we need (state references,
exhaustiveness checks).

### 4. PlantUML / fizzbuzz visual editors

Rejected. None speak SDL well enough to capture the AX.25 spec's particular
notation (guards, action sequences, parameter substitution). Even the best
ones would still demand a transcription step from the bitmap.

## Consequences

### Positive

- **Reviewable in isolation.** One PR per SDL page; reviewer compares YAML to
  PNG.
- **Traceable.** Each transition carries a `source.figure` field linking back
  to the spec page.
- **Test coverage on autopilot.** Every transition gets a generated
  conformance test.
- **Cross-validation.** The codegen tool can lint for unreferenced events,
  unreachable states, missing T1-expiry transitions when T1 is started, etc.
- **LLM-friendly.** The DSL is plain text; agents can propose YAML edits and a
  human can vet them against the figure.

### Negative

- **One more tool to learn.** Contributors must run
  `dotnet run --project tools/Packet.Sdl.CodeGen` before pushing.
- **Schema evolution.** As we encounter cases the schema doesn't model, we
  must extend the schema + the codegen — which may require regenerating
  existing files. Mitigation: codegen is deterministic and idempotent; the
  `git diff --exit-code` guard makes drift impossible to merge.
- **Cannot model every SDL primitive.** Some SDL constructs (e.g. nested
  decision diamonds) may be awkward in YAML. We will flatten them and accept
  small loss of visual fidelity in exchange for machine-readability.

## Status

- 2026-05-11 — Accepted. First SDL (Data-Link `Connected`, figc4.4a) to be
  transcribed as the Phase 0 spike.
