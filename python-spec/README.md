# ax25sdl (Python)

Python consumers of the AX.25 SDL specification, generated from
the YAML transcriptions under `/spec-sdl/` by
`tools/Packet.Sdl.CodeGen.Python`.

## Layout

```
python-spec/
  pyproject.toml
  ax25sdl/
    types.py        # hand-written runtime types — DO NOT regenerate
    __init__.py     # GENERATED — re-exports every page + types
    *.g.py          # GENERATED — one per *.sdl.yaml
    *_g_test.py     # GENERATED — one pytest file per state-machine page
  tests/
    smoke_test.py   # hand-written cross-page smoke tests
```

The `*_g_test.py` filename uses an underscore (not a dot) so each file
matches pytest's default discovery (`*_test.py`); the data files keep
the dotted `.g.py` form so dotfile-based generated-file searches still
match across backends.

## Regenerate

From the repo root:

```sh
dotnet run --project tools/Packet.Sdl.CodeGen -- --python
```

The orchestrator emits C# / Go / TypeScript / Python in the same pass
by default; pass any subset of `--csharp`, `--go`, `--ts`, `--python`
to emit only the named backend(s). CI verifies the generated files are
checked in (idempotent regen + `ruff check` + `ruff format --check` +
`pytest`).

## Local development

```sh
cd python-spec
python3 -m venv .venv && source .venv/bin/activate
pip install -e . pytest ruff
ruff check .
ruff format --check .
python -m pytest -v
deactivate
```

Python 3.11+ is required (`slots=` dataclass kwarg, PEP 604 unions,
`tuple[X, ...]` generics).

## What's not here

This package is **specification data**, not a runtime. It exposes the
SDL transitions, subroutine bodies, predicates, and action verbs as
frozen dataclasses. Building an actual AX.25 session on top requires
binding predicates and action verbs to behaviour (the Python
equivalent of `Packet.Ax25.Session.GuardEvaluator` /
`ActionDispatcher` / `DefaultSubroutineRegistry`) and wiring it to
frame I/O. That work is intentionally out of scope for the codegen;
the goal here is to prove the codegen IR survives another backend.
