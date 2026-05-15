# `@packet-net/ax25-ts`

Browser-targeted TypeScript library for AX.25 v2.2 connected-mode sessions over Web Serial KISS modems.

This is the JavaScript companion to [`Packet.Ax25`](../../src/Packet.Ax25) — the canonical C# implementation in this repo. The two are independent code: TS doesn't share generated SDL tables (those live in [`../../ts-spec/`](../../ts-spec/) and are read-only inspection data, not a runtime). This package is a hand-rolled "good-enough" stack for web apps that want to talk to a TNC connected to the user's USB port.

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

### Implemented (v0.1)

- Frame parser/encoder for U-frames (SABM, UA, DISC, DM, UI), S-frames (RR, RNR, REJ), and I-frames. Mod-8 only.
- 7-octet callsign codec with SSID + C/H + E bit handling.
- KISS framing: FEND (0xC0) / FESC (0xDB) / TFEND / TFESC escape, port nibble.
- `WebSerialKissTransport`: opens a Web Serial port, runs a read loop, KISS-decodes inbound, KISS-encodes outbound.
- Hand-rolled session driver: SABM → UA → Connected, DISC → UA → Disconnected.
- I-frame TX with N(S) + N(R) bookkeeping, k=1 outstanding window, FIFO TX queue.
- Inbound I-frame delivery via `session.onData(...)`.
- Inbound RR acks advance V(a) and pump the next queued frame.
- T1 retry on SABM, DISC, and outstanding I-frame, capped at N2 (default 10).
- Mock transport pair for tests — no Web Serial hardware required to run vitest.

### Out of scope (deliberate — declared, not implemented)

| Missing thing | Behaviour | Why |
| --- | --- | --- |
| **mod-128 (SABME)** | Not exposed. | v2.2 §6.2; very few peers use it. |
| **REJ recovery** | Inbound REJ ack-advances V(a) and triggers an immediate retransmit of the outstanding I-frame, but the proper retransmission of the whole window from N(R) onward is not done. | k=1 means there's only ever one frame to retransmit, so this degrades gracefully. |
| **SREJ** | Not implemented. | Out of scope for v1. |
| **T1 dynamic adjustment** | Fixed at `t1Ms` (default 3000 ms). | §6.7.1 SRT smoothing is high-complexity and unnecessary for the happy path. |
| **FRMR** | Not generated; inbound FRMR is silently dropped. | §4.3.3.6 — peers rarely send it; we don't have anything to do with it. |
| **Multi-frame window (k>1)** | Single outstanding I-frame; subsequent `write()` calls queue locally. | Simpler bookkeeping. |
| **figc4.7 subroutine framework** | Not used. | We're a hand-rolled state machine, not a faithful SDL interpreter. The full implementation is the C# one in `src/Packet.Ax25/Session/`. |
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

- It uses generated SDL tables (`src/Packet.Ax25.Sdl/*.g.cs`) for the state machine.
- It implements figc4.1 (link establishment), figc4.2 (link release), figc4.3 (queue/connected-with-no-outstanding-I), figc4.4 (timer recovery + REJ/SREJ paths), figc4.5 (own busy), figc4.6 (peer busy), figc4.7 (subroutines including `Select_T1_Value`, `Establish_Data_Link`, `Invoke_Retransmission`, `Check_I_Frame_Acknowledged`, etc.).
- It handles XID negotiation, FRMR rejection coding, retried-DISC-counts-as-cleared-link, all the edge cases that matter for interop with real peers (BPQ, Xrouter, direwolf).

This TS library covers ≈10% of that surface — the part a chat-style web app needs.

If you're improving this library, the C# code is the spec reference. Start with:

- [`src/Packet.Ax25/Ax25Frame.cs`](../../src/Packet.Ax25/Ax25Frame.cs) — frame codec.
- [`src/Packet.Core/Ax25Address.cs`](../../src/Packet.Core/Ax25Address.cs) — address codec.
- [`src/Packet.Kiss/KissEncoder.cs`](../../src/Packet.Kiss/KissEncoder.cs) + [`KissDecoder.cs`](../../src/Packet.Kiss/KissDecoder.cs) — KISS framing.
- [`src/Packet.Ax25/Session/Ax25SessionContext.cs`](../../src/Packet.Ax25/Session/Ax25SessionContext.cs) — what state a session carries.
- [`src/Packet.Ax25.Sdl/DataLink_Connected.g.cs`](../../src/Packet.Ax25.Sdl/) — Connected-state transitions (generated from `spec-sdl/datalink_connected.sdl.yaml`).

## Browser compatibility

Web Serial is supported in Chromium-based browsers (Chrome, Edge, Opera, Brave) on desktop OSes. Firefox and Safari don't expose it. The user must grant permission per port via `navigator.serial.requestPort()`.

For non-browser environments (Node.js, Bun, Deno) you'll want a TCP/IP transport instead — implement `Ax25Transport` against a KISS-over-TCP socket. PRs welcome.

## License

MIT
