# `@packet-net/ax25-ts`

Browser-targeted TypeScript library for AX.25 v2.2 connected-mode sessions over Web Serial KISS modems.

This is the JavaScript companion to [`Packet.Ax25`](../../src/Packet.Ax25) — the canonical C# implementation in this repo. As of the table-driven rework the session state machine **walks the generated SDL transition tables in [`../../ts-spec/`](../../ts-spec/)** rather than a hand-rolled switch. The transitions are codegen'd from the same `spec-sdl/` YAML that drives the C# runtime, so future spec transcriptions flow into TypeScript automatically — no manual edits to `session.ts` for new states, events, or guards.

## Status

Stretch-goal experiment. Functional for the happy path; missing chunks of AX.25 v2.2. See "What's in / what's out" below.

## Install

This package is not yet published to npm. Inside this monorepo:

```sh
cd web/ax25-ts
npm install
npm run build
npm test
```

## Quick start

```ts
import {
  Ax25Stack,
  Callsign,
  WebSerialKissTransport,
} from "@packet-net/ax25-ts";

const port = await navigator.serial.requestPort();
const transport = new WebSerialKissTransport(port, { baudRate: 9600 });
const stack = new Ax25Stack(transport);
await stack.start();

const session = await stack.connect({
  from: Callsign.parse("M0LTE-2"),
  to: "GB7CIP",
});

session.onData((chunk) => {
  console.log(new TextDecoder().decode(chunk));
});
session.onDisconnected(() => {
  console.log("link closed");
});

await session.write(new TextEncoder().encode("hello\r"));

// Later:
await session.disconnect();
await stack.stop();
```

The full typechecked example is in [`examples/quick-start.ts`](examples/quick-start.ts).

## Public API

| Export | Purpose |
| --- | --- |
| `Callsign` | 0-6 char base + 0-15 SSID. `Callsign.parse("M0LTE-7")`. |
| `Ax25Frame` (interface) | A parsed AX.25 frame: destination, source, digipeaters, control, pid, info. |
| `sabm / disc / ua / dm / ui / rr / rnr / rej / iFrame` | Frame factories. |
| `encodeFrame / decodeFrame` | Round-trip an `Ax25Frame` to/from wire bytes (no FCS — KISS form). |
| `classify` | `"SABM" | "DISC" | "UA" | "DM" | "UI" | "RR" | "RNR" | "REJ" | "I" | "UNKNOWN"`. |
| `KissDecoder`, `encodeKiss` | KISS framing (FEND/FESC escape). |
| `Ax25Transport` | 3-method interface (`start / send / stop`). |
| `WebSerialKissTransport` | KISS over a `SerialPort`. |
| `Ax25Stack` | Owns the transport. `connect({ from, to })` → `Promise<Ax25Session>`. |
| `Ax25Session` | `onData(cb)`, `onDisconnected(cb)`, `write(bytes)`, `disconnect()`. |

## What's in / what's out / what's known to be wrong

### Implemented (v0.2)

- Frame parser/encoder for U-frames (SABM, UA, DISC, DM, UI), S-frames (RR, RNR, REJ), and I-frames. Mod-8 only.
- 7-octet callsign codec with SSID + C/H + E bit handling.
- KISS framing: FEND (0xC0) / FESC (0xDB) / TFEND / TFESC escape, port nibble.
- `WebSerialKissTransport`: opens a Web Serial port, runs a read loop, KISS-decodes inbound, KISS-encodes outbound.
- **Table-driven session machine** — the driver in [`src/sdl/session-driver.ts`](src/sdl/session-driver.ts) imports `DataLinkDisconnected`, `DataLinkAwaitingConnection`, `DataLinkAwaitingConnection22`, `DataLinkConnected`, and `DataLinkAwaitingRelease` from the [`ax25sdl`](../../ts-spec/) package, looks up the transitions out of the current state for each posted event, evaluates guard expressions like `command and info_field_valid and V_a_le_N_r_le_V_s` against a bindings dictionary, executes the action chain via [`src/sdl/action-dispatcher.ts`](src/sdl/action-dispatcher.ts), and advances state. This is the same architecture the C# runtime uses (see `src/Packet.Ax25/Session/`).
- SABM → UA → Connected, DISC → UA → Disconnected, I-frame TX/RX with V(s)/V(r)/V(a) bookkeeping, k=1 outstanding window, FIFO TX queue.
- Inbound I-frame delivery via `session.onData(...)`, inbound RR acks via the `Check_I_Frame_Acknowledged` inlined subroutine (advances V(a), pumps the next queued frame).
- T1 retry on SABM, DISC, and outstanding I-frame, capped at N2 (default 10) via the SDL `RC_eq_N2` guard.
- Mock transport pair for tests — no Web Serial hardware required to run vitest.

### File layout

```
src/
├── address.ts                 Ax25Address record + codec
├── callsign.ts                Callsign type
├── frame.ts                   Ax25Frame, factories, encode/decode, classify
├── kiss.ts                    KISS framing (FEND/FESC/TFEND/TFESC)
├── transport.ts               Ax25Transport interface
├── webserial-transport.ts     KISS over Web Serial port
├── session.ts                 Public Ax25Stack / Ax25Session — wraps the SDL driver
└── sdl/                       Table-walking session engine
    ├── events.ts                  Ax25Event variants (frame-receipt, timer, DL primitives)
    ├── timer-scheduler.ts         T1/T2/T3 arming + isRunning bindings
    ├── session-context.ts         Mutable per-session state (V(S)/V(A)/V(R), flags, queues)
    ├── guard-evaluator.ts         Parses `"a and not b or c"` against a bindings dict
    ├── session-bindings.ts        Builds the dict — predicate name → closure
    ├── action-dispatcher.ts       Switch over the ~140 SDL action verbs
    ├── subroutine-registry.ts     figc4.7 subroutine stub registry (gaps documented)
    └── session-driver.ts          PostEvent → find transition → execute → advance
```

### Why table-driven?

Three reasons:

1. **Spec faithfulness.** The SDL figures in AX.25 v2.2 appendix C define the state machine exactly. The transitions in `ts-spec/src/ax25sdl/*.g.ts` are codegen'd from human-authored transcriptions of those figures (see [`spec-sdl/`](../../spec-sdl/)). Walking the tables means we behave exactly as the figures say.

2. **No manual sync.** Adding a new transition or fixing a transcribed guard now flows automatically into this library on the next codegen run. The old hand-rolled `switch (state)` had to be kept in sync by hand.

3. **Same source of truth as C#.** The C# runtime in `src/Packet.Ax25/Session/Ax25Session.cs` walks the same tables (its own .g.cs flavour). If a peer-interop issue gets fixed in the SDL YAML, both runtimes benefit on the next codegen.

### Out of scope (deliberate — declared, not implemented)

| Missing thing | Behaviour | Why |
| --- | --- | --- |
| **mod-128 (SABME)** | Not exposed. The SDL's `version_2_2` / `mod_128` predicates always return `false`, so mod-128 branches in the transition tables are routed-around. | v2.2 §6.2; very few peers use it. |
| **figc4.7 subroutines** | The dispatcher **inlines** `Establish_Data_Link` and `Check_I_Frame_Acknowledged` because the happy path needs them. The rest (`Select_T1_Value`, `Transmit_Enquiry`, `Invoke_Retransmission`, `N_r_Error_Recovery`, `Enquiry_Response`, `Check_Need_For_Response`, `UI_Check`, `Set_Version_2_*`) route through a no-op stub registry. | Walking figc4.7's paths through the same dispatcher is the next obvious follow-up but isn't in this PR. The C# `DefaultSubroutineRegistry` is the model. |
| **REJ / SREJ recovery** | Whatever the figc4.4 tables say happens. Since `Enquiry_Response`, `Invoke_Retransmission`, and `N_r_Error_Recovery` are stubbed, full REJ/SREJ recovery is incomplete — some paths emit the right wire frames (REJ, SREJ) but the recovery loop is a no-op. Documented as a follow-up. | Implementation needed in the dispatcher / subroutine registry. |
| **T1 dynamic adjustment** | The `Select_T1_Value` subroutine is a no-op stub, so T1V stays at the initial value. The dispatcher's `freezeT1V` flag suppresses the spec's `SRT := Initial Default` / `T1V := 2 * SRT` actions so a caller-supplied `t1Ms` is honoured. | RTT smoothing is high-complexity and unnecessary for the happy path. |
| **FRMR** | Not generated; inbound FRMR is silently dropped. | §4.3.3.6 — peers rarely send it; we don't have anything to do with it. |
| **Multi-frame window (k>1)** | Hard-coded `k=1` in the session constructor. Subsequent `write()` calls queue locally. | The SDL guard `V_s_eq_V_a_plus_k` reads `ctx.k` — change the assignment in `session.ts` to widen. |
| **Digipeater paths (`via`)** | `stack.connect({ via: [...] })` **throws "not implemented"**. | Frame factories accept digipeaters and the codec round-trips them — only the session driver doesn't route through them. |
| **TCP / AGW / AXUDP / audio transports** | Web Serial only. | Define `Ax25Transport` and slot in your own. |
| **Inbound connection acceptance** | A SABM addressed to us with no matching outbound session is silently dropped. | No `onConnectRequest` API yet. |
| **XID negotiation** | Not implemented. | Defaults are used (mod-8, no SREJ). |

### Known gotchas

- **K=1 window is a real limitation.** Throughput on a real link will be SRT-bound (each frame waits for the peer's RR before the next frame leaves). Reasonable for interactive BBS sessions; awful for bulk transfer.
- **Out-of-sequence inbound I-frames** are answered with RR(N(R)=V(r)) instead of REJ. The peer's T1 eventually retransmits the missing frame. This is "works, but slower than spec".
- **`MockTransport` uses `queueMicrotask`**, not `setTimeout(0)`. If you write a test that needs the round-trip to interleave with a `setTimeout`, you may need extra `await peer.flush()` calls. Two flushes is usually enough.
- **Web Serial port lifecycle:** if `start()` resolves and the user yanks the cable, the read loop just exits silently and the session's T1 will eventually fire. There's no `onTransportError` callback — this is a gap.

### How does this compare to the C# reference?

`src/Packet.Ax25/Session/` is the faithful spec implementation:

- It walks the same SDL tables this library now walks (its own `.g.cs` flavour from the same codegen).
- It implements figc4.7 subroutines for real (`Select_T1_Value`, `Invoke_Retransmission`, `Check_I_Frame_Acknowledged`, `Transmit_Enquiry`, etc.) — so REJ/SREJ recovery, dynamic T1V, and proper enquiry-response work.
- It handles XID negotiation, FRMR rejection coding, retried-DISC-counts-as-cleared-link, all the edge cases that matter for interop with real peers (BPQ, Xrouter, direwolf, rax25).

This TS library walks the same transitions but **stubs the figc4.7 subroutines**. That means it covers ≈80% of the wire behaviour — enough for chat-style BBS / node-prompt usage — but it doesn't yet handle REJ/SREJ recovery loops, dynamic T1V, or anything else that requires walking the subroutine paths.

If you're improving this library, the C# code is the spec reference. Start with:

- [`src/Packet.Ax25/Session/Ax25Session.cs`](../../src/Packet.Ax25/Session/Ax25Session.cs) — the table-walking driver. Same shape as `src/sdl/session-driver.ts`.
- [`src/Packet.Ax25/Session/ActionDispatcher.cs`](../../src/Packet.Ax25/Session/ActionDispatcher.cs) — the action-verb switch. Same shape as `src/sdl/action-dispatcher.ts`.
- [`src/Packet.Ax25/Session/Ax25SessionBindings.cs`](../../src/Packet.Ax25/Session/Ax25SessionBindings.cs) — guard predicate bindings.
- [`src/Packet.Ax25/Session/SubroutineRegistry.cs`](../../src/Packet.Ax25/Session/SubroutineRegistry.cs) — figc4.7 wiring (NOT yet ported to TS).
- [`src/Packet.Ax25.Sdl/DataLink_Connected.g.cs`](../../src/Packet.Ax25.Sdl/) — Connected-state transitions (the C# flavour of what `ts-spec/src/ax25sdl/connected.g.ts` contains).

## Browser compatibility

Web Serial is supported in Chromium-based browsers (Chrome, Edge, Opera, Brave) on desktop OSes. Firefox and Safari don't expose it. The user must grant permission per port via `navigator.serial.requestPort()`.

For non-browser environments (Node.js, Bun, Deno) you'll want a TCP/IP transport instead — implement `Ax25Transport` against a KISS-over-TCP socket. PRs welcome.

## License

MIT
