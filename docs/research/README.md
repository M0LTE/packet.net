# Research / options analyses

Exploratory design-space write-ups that inform the AX.25 roadmap (`../plan.md`) but are **not** commitments — they capture investigation + options + a recommendation at a point in time, for later decision. Each is dated and grounded against the live `m0lte/ax25sdl`, `m0lte/packet.net`, and `m0lte/ax25-ts` trees plus the AX.25 v2.2 spec.

| Doc | Question | Headline |
|---|---|---|
| [pico-packet-node.md](pico-packet-node.md) | Can a lightweight but fully v2.2-compliant packet node run on Pi Pico W-class hardware (RP2040), reusing this stack? | Feasible — Rust + Embassy (`no_std`) consuming the generated tables; AXUDP-over-WiFi tier first; "fully compliant" + "fits 264 KB" reconciled by bounding N1/k via XID. The work is the *runtime*, not the tables. |
| [codegen-reach.md](codegen-reach.md) | Beyond the SDL state-machine tables, what else is spec-derived enough to codegen across languages (shrinking the hand-written per-language runtime)? | Generate **cross-language conformance vectors first** (drift-proofs the un-generatable majority); then the SP-010 guard/event closed sets; then the frame + XID wire codecs. |

Both were produced during the v2.2 completion arc (2026-06); the codegen-reach analysis is the companion to the Pico one ("the work is the runtime, not the tables").
