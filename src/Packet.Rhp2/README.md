# Packet.Rhp2

The **RHPv2** (Radio Host Protocol v2 — PWP-0222 / PWP-0245) JSON-over-TCP **wire codec**: the single source of truth for the RHPv2 wire format, with no engine, no transport, and no client/server policy. It is the shared codec layer both pdn's RHPv2 server (`Packet.Rhp2.Server`) and RHPv2 clients build on.

## What it does

- **Framing** (`RhpFraming`): each message is a 2-byte big-endian unsigned length prefix followed by exactly that many bytes of UTF-8 JSON. A zero-length frame is legal; the reader bounds a mid-frame stall (slowloris) without bounding idle-between-frames waits.
- **Message catalogue** (`RhpMessage` + the per-type DTOs): `auth`, `open`, `socket`, `bind`, `listen`, `connect`, `send`, `sendto`, `recv`, `accept`, `status`, `close`, and their replies. An unrecognised `type` becomes an `UnknownMessage` carrying the raw JSON (forward-compatible) rather than throwing.
- **JSON codec** (`RhpJson`): type-first key emission, `WhenWritingNull` omission (matching XRouter, which never writes JSON nulls), case-insensitive reads.
- **Payload encoding** (`RhpDataEncoding`): bytes ↔ a Latin-1 string in the JSON `data` field — one byte per code unit, JSON escaping does the rest. **Not base64.**
- **Constants** (`RhpConstants`): `ProtocolFamily`, `SocketMode`, `OpenFlags`, `StatusFlags`, `RhpErrorCode` (with canonical `Text()`), `RhpMessageType`.

## Wire fidelity

The shapes here are pinned against **live XRouter**, not just the published spec — capital `errCode`/`errText` on every reply, the `connectReply` PascalCase-typo tolerance on read, `port` string-or-number normalisation, `errCode 17` "Not connected", and the Latin-1 (not base64) `data` encoding. See `docs/rhp2-server.md` in the source repo for the full spec-vs-wire delta table.

## Relationship to rhp2lib

`Packet.Rhp2` and rhp2lib-net's embedded codec (`RhpV2.Client.Protocol`) are **byte-identical on the wire** — same framing, same Latin-1 encoding, same `RhpErrorCode` values and text, same type-first dispatch. This package is published so the two can converge onto one codec instead of maintaining divergent copies (packet-net/packet.net#474).
