# Packet.AprsIs.Spike

SP-001b — APRS-IS UI-frame corpus ingestion (see `docs/plan.md` §5.X).

Connects to APRS-IS as a read-only listener, parses TNC2-format frames,
attempts to reconstruct each as `Ax25Frame.Ui(...)`, round-trips through
our parser, and reports parser robustness against real-world traffic.

The point isn't to be an APRS gateway — it's to drive a steady stream of
real-world data through our AX.25 plumbing and surface edge cases that
synthetic tests don't cover.

## Run

```sh
dotnet run --project tools/Packet.AprsIs.Spike -- --max-frames 1000
```

Outputs land in `artifacts/aprs-is-spike/<timestamp>/`:

- `failures.jsonl` — one JSON line per parser/reconstruct/round-trip failure.
- `stats.md` — final summary.

### Args

| Flag | Default | Meaning |
|---|---|---|
| `--server <host:port>` | `rotate.aprs2.net:14580` | APRS-IS server |
| `--callsign <CALL>` | `N0CALL` | login callsign (read-only via passcode -1) |
| `--filter <filter>` | `t/poimqstuc` | server-side filter (all APRS message types) |
| `--max-frames <N>` | `1000` | stop after N frames (0 = unlimited) |
| `--out-dir <dir>` | `artifacts/aprs-is-spike/<ts>/` | artifact directory |
| `--quiet` | off | suppress per-frame stdout |

## Known finding: APRS letter SSIDs

A first 30-frame smoke run produced **8 reconstruct failures, all from D-Star
gateways** using APRS letter SSIDs (`-B`, `-D`, `-T`, `-H`) — e.g.:

```
F5ZMN-SH>APSF40,TCPIP*,qAC,T2HUN:>Hardware: ESP8266 + BME280…
IZ6BDZ-B>APDG02,TCPIP*,qAC,IZ6BDZ-BS:!5000.00ND…  (D-Star B port)
DN9AMB-D>APDG03,TCPIP*,qAC,DN9AMB-DS:!5056.88N…   (D-Star D port)
```

These are **APRS conventions** that aren't valid AX.25 source addresses —
the spec only allows numeric SSIDs 0–15. APRS-IS leaks them through. Our
`Callsign.TryParse` correctly rejects them; the spike records each failure
to `failures.jsonl` for visibility.

This is the kind of real-world quirk the spike is designed to surface — the
existing synthetic tests don't generate letter SSIDs. Possible follow-ups:

- A separate "AprsCallsign" type that accepts letter SSIDs, for the APRS
  monitor view in the web UI (which needs to display these verbatim).
- Mapping rules from APRS letter-SSID to AX.25 numeric-SSID at the gateway
  layer (probably `B → 11`, `C → 12`, `D → 13` per common D-Star practice).

## Scope

v0.1 spike — UI frames only (the only frame type APRS-IS carries). No
persistence beyond `artifacts/`. No live ingestion to a regression corpus
(that's SP-003's job). Will be retired or graduated to `src/Packet.Replay.AprsIs/`
once the patterns are clear.
