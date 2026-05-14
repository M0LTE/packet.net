# go-spec

Go consumers of the AX.25 SDL specification, generated from the YAML
transcriptions under `/spec-sdl/` by `tools/Packet.Sdl.CodeGen.Go`.

## Layout

```
go-spec/
  go.mod
  ax25sdl/
    types.go     # hand-written runtime types — DO NOT regenerate
    *.g.go       # generated state-machine + subroutine pages
```

## Regenerate

From the repo root:

```sh
dotnet run --project tools/Packet.Sdl.CodeGen -- \
  --in spec-sdl \
  --out src/Packet.Ax25.Sdl \
  --tests tests/Packet.Ax25.Conformance.Tests \
  --go go-spec/ax25sdl
```

The orchestrator emits C# and Go in the same pass so the two stay in
lockstep. CI verifies the generated files are checked in (idempotent
regen + `go vet ./...` + `go test ./...`).

## What's not here

This module is **specification data**, not a runtime. It exposes the
SDL transitions, subroutine bodies, predicates, and action verbs as Go
data structures. Building an actual AX.25 session on top requires
binding predicates and action verbs to behaviour (the Go equivalent of
`Packet.Ax25.Session.GuardEvaluator` / `ActionDispatcher` /
`DefaultSubroutineRegistry`) and wiring it to frame I/O. That work is
out of scope for Tier 1b; the goal here is to prove the codegen
pipeline is language-agnostic and that the IR survives a second
backend.
