# c-spec

C consumers of the AX.25 SDL specification, generated from the YAML
transcriptions under `/spec-sdl/` by `tools/Packet.Sdl.CodeGen.C`.

## Layout

```
c-spec/
  CMakeLists.txt        # hand-written
  include/
    ax25sdl.h           # hand-written runtime types — DO NOT regenerate
  src/
    *.g.c               # generated state-machine + subroutine pages
    ax25sdl.g.h         # generated master header (extern decls)
  test/
    *.g.test.c          # generated per-state test executables
    smoke.test.c        # hand-written cross-page smoke checks
```

## Regenerate

From the repo root:

```sh
dotnet run --project tools/Packet.Sdl.CodeGen -- --c
```

## Build and test

```sh
cd c-spec
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build --parallel
ctest --test-dir build --output-on-failure
```

Requires `cmake` (≥ 3.20) plus a C99-capable toolchain (`gcc` or
`clang`). `clang-format` is optional — the codegen will use it to
canonicalise the generated sources if it's on PATH, otherwise the
in-emitter formatting is used as-is.

## What's not here

This module is **specification data**, not a runtime. It exposes the
SDL transitions, subroutine bodies, predicates, and action verbs as C
data structures. Building an actual AX.25 session on top requires
binding predicates and action verbs to behaviour (the C equivalent of
`Packet.Ax25.Session.GuardEvaluator` / `ActionDispatcher` /
`DefaultSubroutineRegistry`) and wiring it to frame I/O. That work is
out of scope; the goal here is to prove the codegen pipeline is
language-agnostic and that the IR survives a fourth backend.
