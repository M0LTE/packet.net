# APRS-IS corpus — early findings

First proper analyser pass. Curated narrative kept under source control;
raw stats.md / failures.jsonl land in `artifacts/aprs-is-analysis/<ts>/`
(gitignored).

Re-run with `dotnet run --project tools/Packet.AprsIs.Spike -- analyse`.

## 2026-05-14 — first 63k lines (~12 minutes of capture)

| Metric | Count | % of total |
|---|--:|--:|
| Lines processed | 63,451 | 100.00 % |
| TNC2 parse failures | 0 | 0.00 % |
| Reconstruct failures | 14,210 | **22.40 %** |
| Round-trip failures | 0 | 0.00 % |
| Round-trip successes | **49,241** | **77.60 %** |

**Headline**: ~22 % of real APRS-IS traffic uses callsign/SSID patterns
that aren't valid under the AX.25 spec. Of those failures, **99.91 % are
invalid sources** — destinations and digipeaters are fine almost across
the board.

Of the lines that *do* reconstruct, every single one round-trips through
`Ax25Frame.TryParse` losslessly. The parser/builder pair is solid; the
gap is at the AX.25 ↔ APRS-convention boundary.

## Failure categories (sources)

By inspecting top offenders against the 14,197 invalid-source failures:

### 1. Lowercase callsigns (~700 lines)

Sources like `db0sda` (600 lines — a German DV repeater beaconing
constantly), `vk3mak-15`, `dl9mfl-6`, `iw0uwf-4`, `iz1wiy-3` etc. AX.25
requires uppercase A–Z; APRS-IS happily passes them through.

### 2. Tactical / non-callsign source addresses (~480 lines)

`WINLINK` (449 lines), `ONELOVE`, `DONTKILL`, `NorthRyde`, `EL-S55MA`.
Winlink RMS gateways advertise their packet presence using `WINLINK` as
a tactical alias. None of these are valid AX.25 addresses.

### 3. Multi-character / non-numeric SSIDs (~13,000 lines — the bulk)

Patterns like `M0IQF-N4`, `DD9PX-77`, `BI4KVT-8G`, `BD8CMN-T`,
`BA4QFV-O`. AX.25 spec allows SSIDs 0–15 numeric only. APRS uses:

- Letter SSIDs (`-A`, `-B`, `-T`, `-N`) for D-Star / DMR gateways, RMS
  stations, weather, etc.
- Multi-digit SSIDs (`-77`, `-20`, `-48`) used by Chinese / Russian
  stations and APRS firmwares.
- Combinations (`-8G`, `-N4`, `-N0`, `-S55MA`).

This is the largest class and the most thorny — APRS conventions
genuinely vary by country and software. Worth filing as an upstream
APRS-spec issue if "permitted SSID forms" aren't documented.

### 4. Long base callsigns (> 6 chars)

`BD8AWU-18`, `BD8CMN-S/T/H`, `BI4KVT-8G`. Spec is strict 1–6 chars.
Often overlaps category 3.

## Destination / digipeater failures (rare)

- 5 invalid destinations seen (`AP4R132`, `APLRd1`, `APOT212`, `UQSWT63`):
  long destination "to" calls — APRS software pads the destination with
  software-ID tokens that occasionally exceed 6 chars.
- 8 invalid digipeaters: lowercase (`wide1-1`), letter SSIDs
  (`DO0SAS-L4`, `9M2VKA-L`), multi-digit SSIDs (`OK2ZAW-17`).

## What this tells us about the AX.25 parser

The strict `Callsign.TryParse` is **correctly** rejecting these — the
AX.25 spec is unambiguous about A–Z / 0–9 and SSID 0–15. Our parser is
behaving as designed.

But for the *monitor* layer (e.g. a web UI showing live APRS-IS frames),
strict rejection means ~22 % of traffic disappears. The right shape for
the codebase is:

- **`Callsign`** — strict AX.25 type. Stays as-is.
- **`AprsCallsign`** — looser type accepting the APRS conventions above.
  Lives in the eventual `Packet.Aprs` library (SP-008).
- **A boundary layer** that maps `AprsCallsign` → `Callsign` where
  possible (`-B` → `-11`, etc., per common D-Star practice), and surfaces
  the raw `AprsCallsign` to UI consumers verbatim where not.

## What this tells us about the corpus

77.60 % clean parse is encouraging baseline. The remaining 22.40 % is
exactly the kind of real-world data the spike was built to surface —
each failure mode is now a documented edge case rather than a
hypothetical.

The corpus itself is feedstock for:

- **SP-002** (direwolf-as-reference harness) — A/B our parser against
  direwolf on the same TNC2 lines.
- **SP-003** (replay/regression harness) — once we have the
  `Packet.Replay.AprsIs` library, every captured line becomes a regression
  test fixture.
- **SP-004** (fuzz harness) — the corpus is a high-quality seed for
  SharpFuzz against `Ax25Frame.TryParse` (real-world inputs > synthetic
  inputs at finding edge cases).
- **SP-008** (full APRS library) — `AprsCallsign` design driven by what
  we actually see, not what we *think* the APRS spec allows.

## How to re-run

Against the live corpus:

```sh
dotnet run --project tools/Packet.AprsIs.Spike -- analyse \
  --data-dir /home/tf/aprs-is-data \
  --out-dir /tmp/aprs-analysis-$(date -u +%Y%m%d-%H%M%S)
```

Or one specific day:

```sh
dotnet run --project tools/Packet.AprsIs.Spike -- analyse \
  --db /home/tf/aprs-is-data/aprs-is-2026-05-14.sqlite
```

Reports go to `<out-dir>/stats.md` (markdown summary) +
`<out-dir>/failures.jsonl` (one line per failure with raw input + reason).
