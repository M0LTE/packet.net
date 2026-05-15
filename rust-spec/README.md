# rust-spec

Rust consumers of the AX.25 SDL specification, generated from the YAML
transcriptions under `/spec-sdl/` by `tools/Packet.Sdl.CodeGen.Rust`.

## Layout

```
rust-spec/
  Cargo.toml
  src/
    types.rs     # hand-written runtime types — DO NOT regenerate
    lib.rs      # generated module list + re-exports
    *.g.rs      # generated state-machine + subroutine pages (data + tests)
  tests/
    smoke.rs    # hand-written cross-page smoke tests
```

## Regenerate

From the repo root:

```sh
dotnet run --project tools/Packet.Sdl.CodeGen -- --rust
```

The orchestrator emits the C#, Go, TypeScript, and Rust backends in the
same pass when invoked without language flags; passing `--rust` opts in
to just the Rust backend. CI verifies the generated files are checked
in (idempotent regen + `cargo build` + `cargo test` + `cargo fmt --check`).

## What's not here

This crate is **specification data**, not a runtime. It exposes the SDL
transitions, subroutine bodies, predicates, and action verbs as Rust
`pub static` values backed by `&'static [..]` slices. Building an actual
AX.25 session on top requires binding predicates and action verbs to
behaviour (the Rust equivalent of `Packet.Ax25.Session.GuardEvaluator`
/ `ActionDispatcher` / `DefaultSubroutineRegistry`) and wiring it to
frame I/O. That work is out of scope for the codegen pipeline; the
goal here is to prove the IR survives a third backend.
