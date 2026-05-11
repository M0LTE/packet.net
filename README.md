# Packet.NET

A modern, reusable AX.25 2.2 stack and packet-radio node, written in .NET 10.

**Status:** pre-alpha — phase 0 scaffolding. No usable release yet.

## What this is

Packet.NET is two things:

1. **A set of reusable .NET libraries** implementing AX.25 2.2, KISS (including
   ACKMODE and multi-drop), AXUDP, and external-application protocols (RHPv2,
   AGW). Each library is independently publishable on NuGet under the
   `Packet.*` prefix.

2. **A packet-radio node** built on those libraries, with a modern web UI for
   configuration and operation, a TNC2-style command prompt, REST API, MCP
   server, and pluggable application modules (BBS, chat, DAPPS, etc. — none of
   which ship in the core).

The node is designed for **real-world half-duplex, lossy, shared-medium**
KISS+ACKMODE radio links. It's tested against LinBPQ, Xrouter, net-sim, and a
back-to-back pair of NinoTNCs in CI.

## Goals

- Zero file editing for configuration — everything is web-based.
- One-line install: `curl … | sudo bash`, plus a Docker image.
- Cross-platform: Linux (x64 / arm64 / armhf), Windows x64, macOS arm64 + x64.
  systemd service on Linux (Debian / Ubuntu / Raspberry Pi OS first class).
- A first-rate REST API with modern auth (Argon2id + WebAuthn/passkeys + JWT).
- A built-in MCP server for ops, diagnostics, and network exploration.
- Live packet monitoring + link troubleshooting that go beyond plain frame
  tracing.
- Extensive interoperability tests — every PR runs interop against LinBPQ /
  Xrouter / net-sim, and a 72-hour soak runs nightly against a long-running
  LinBPQ peer.

## What this is NOT

- A BBS, chat server, mailbox, or DAPPS implementation — those live as
  out-of-tree plugins.
- An HF waveform stack — Packet.NET talks to KISS modems (over TCP / serial)
  and AXUDP only. VARA, ARDOP, and friends are out of scope for v1.
- A drop-in LinBPQ replacement — Packet.NET aims for protocol-level
  interoperability, not bug-for-bug feature parity.

## Roadmap

**[`docs/plan.md`](docs/plan.md) is the authoritative living plan for the
project** — direction, status, working agreements, glossary, reference shelf,
and amendment log. Read it before contributing.

## Building

```sh
dotnet build
dotnet test
```

Requires the .NET 10 SDK (see `global.json`).

## License

MIT — see `LICENSE`.

## Acknowledgements

- The [packethacking](https://github.com/packethacking) AX.25 2.2 specification
  rewrite.
- John Wiseman G8BPQ for LinBPQ, decades of packet work, and the multi-drop
  KISS / ACKMODE extensions.
- The Open Amateur Radio Community (OARC).
