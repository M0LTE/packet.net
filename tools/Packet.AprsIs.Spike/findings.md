# APRS-IS corpus — early findings

First proper analyser pass. Curated narrative kept under source control;
raw stats.md / failures.jsonl land in `artifacts/aprs-is-analysis/<ts>/`
(gitignored).

Re-run with `dotnet run --project tools/Packet.AprsIs.Spike -- analyse`.

## 2026-05-14 — payload-type breakdown (181k lines, ~30 min capture)

First run with the payload-type classifier wired in (Tier 0 unblocker).
Bucketed by the first information-field byte per APRS101 §5 DTI.

| Type | Count | % of classified | Group |
|---|--:|--:|---|
| `position_no_ts_no_msg` | 54,906 | 30.29 % | positions |
| `position_ts_msg` | 27,222 | 15.02 % | positions |
| `position_no_ts_msg` | 27,021 | 14.91 % | positions |
| `position_ts_no_msg` | 2,858 | 1.58 % | positions |
| `object` | 19,511 | 10.76 % | reports |
| `message` | 14,988 | 8.27 % | messaging |
| `status` | 13,834 | 7.63 % | reports |
| `telemetry` | 10,367 | 5.72 % | telemetry |
| `mic_e_current` | 7,877 | 4.35 % | mic-E |
| `item` | 1,501 | 0.83 % | reports |
| `mic_e_old` | 943 | 0.52 % | mic-E |
| `user_defined` | 233 | 0.13 % | other |
| `raw_gps_or_ultimeter` | 9 | 0.00 % | other |
| `third_party` | 1 | 0.00 % | other |

**Highlights:**

- **Positions dominate**: 4 variants total **61.80 %** of all corpus traffic.
  The decoder ordering is now clear — start with `position_no_ts_no_msg`
  (the `!` DTI, simplest format), then `position_ts_*` (timestamp
  variants), then mic-E (compressed binary).
- **Messages are 8.27 %** — a real chunk. Implementing them needs the
  ack/reject state machine (APRS messages have retry semantics on top of
  AX.25 UI frames).
- **Mic-E is ~4.9 %** combined — smaller than I'd guessed. Mic-E is
  bigger on local RF than on the APRS-IS firehose (probably because
  Mic-E is more popular for mobile, and APRS-IS is sourced from igates
  globally).
- **Zero `non_printable_*` or `empty`** rows means every TNC2 line we
  captured had a printable-ASCII first byte of payload. Good signal —
  the corpus is "well-formed APRS as APRS-IS sees it".

Reconstruct success rate held steady at **78.05 %** (was 77.60 % at
the smaller sample). The 22 % miss rate is structural (APRS-vs-AX.25
callsign conventions), not statistical noise.

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
