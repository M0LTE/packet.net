# Contributing to Packet.NET

Thanks for the interest — Packet.NET is a fresh codebase and contributions are
welcome. Please read this short guide before opening a PR.

**Before anything else, read [`docs/plan.md`](docs/plan.md).** It is the
authoritative living plan: direction, status, working agreements, glossary,
amendment log. The working-agreements section in particular contains hard
discipline that PRs are expected to follow.

## Ground rules

- Be kind. This project leans on the amateur-radio + packet community; treat
  it like a club.
- Open an issue for anything non-trivial before writing code. Drive-by PRs that
  reshape an interface or add a new library will likely bounce.
- Keep PRs small and focused. One concern per PR.

## Building

```sh
dotnet build
dotnet test
```

Requires .NET 10 SDK 10.0.203 or later (see `global.json`).

## House style

- C# 14, nullable enabled, warnings-as-errors. Don't silence analyzer warnings
  without an explanation.
- One library per concern, each independently NuGet-publishable under the
  `Packet.*` prefix. The repo layout in `docs/plan.md` is the source of truth.
- Tests live under `tests/` mirroring the source layout (`Packet.Foo` →
  `tests/Packet.Foo.Tests`).
- Public APIs need XML doc comments. Internal helpers don't.
- No `var` in places where the type is non-obvious to a reader who hasn't read
  the surrounding file.
- No emojis in source or commit messages.
- No comments that just narrate what the code does. Comments are for *why*,
  not *what*.

## Tests

- Unit tests live alongside the library: `Packet.Foo.Tests`.
- Property tests (FsCheck) live in `Packet.Foo.Properties` (e.g.
  `Packet.Ax25.Properties`).
- Interop tests (Testcontainers driving LinBPQ / Xrouter / net-sim) live in
  `Packet.Interop.Tests`.
- Hardware-loop tests are gated with `[Trait("Category", "HardwareLoop")]` so
  ordinary PRs don't need the physical TNC pair.
- Tests must NOT rely on global state or `Thread.Sleep`. Use timer abstractions.

## Working with SDL diagrams

The AX.25 2.2 specification contains 27 SDL state diagrams that we transcribe
into a small YAML DSL under `/spec-sdl/`. See `docs/adr/0001-sdl-dsl.md` for
the rationale and `docs/sdl-primer.md` for the SDL shape reference you'll need
to read the figures.

Workflow:
1. Open a PR titled `sdl(<machine>/<state>): transcribe <figure>` containing
   ONLY the new/updated `*.sdl.yaml`.
2. CI regenerates `src/Packet.Ax25.Sdl/*.g.cs` and runs the conformance tests.
3. The PR comment will show the figure PNG side-by-side with the YAML diff.
4. A maintainer reviews the YAML against the spec figure.
5. On merge, CI commits the regenerated `*.g.cs` (or the PR includes them
   pre-regenerated — `dotnet run --project tools/Packet.Sdl.CodeGen` locally).

Never edit `src/Packet.Ax25.Sdl/*.g.cs` by hand. CI enforces this with
`git diff --exit-code` after regeneration.

## Commit messages

Conventional Commits-ish, but loose:

```
ax25: add SREJ handling for mod-128
sdl(data-link/connected): transcribe figc4.4a
kiss: handle FESC followed by EOF
ci: bump testcontainers
```

Avoid messages like "fix bug" or "wip".

## Reviewing PRs

- Read the SDL YAML against the spec figure for SDL transcription PRs.
- For protocol changes, verify against the LinBPQ interop harness output
  (CI artifact).
- Question new dependencies — Packet.NET aims for a small dependency surface.

## Security

If you find a vulnerability, please email the maintainer privately rather than
opening a public issue. See `SECURITY.md` (once it exists).

## Keeping the plan current

`docs/plan.md` is the project's authoritative living document. Every PR that
materially changes direction, scope, locked decisions, working agreements,
risks, dependencies, or completes a phase exit criterion **must** update
`docs/plan.md` in the same PR — including an entry in §17 Amendment log. See
§18 of the plan for the discipline.

If your PR ships without a plan update and the change is plan-relevant, the
review will ask for one before merging.
