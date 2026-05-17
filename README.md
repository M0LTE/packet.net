# Packet.NET

A modern, reusable AX.25 2.2 stack and packet-radio node, written in .NET 10.

**Status:** early. Libraries are publishing to NuGet; the node host is taking shape. See [`docs/plan.md`](docs/plan.md) for the up-to-date roadmap.

## What this repo holds

After the 2026-05-17 multi-repo split, **`m0lte/packet.net` is the .NET libraries + the packet node**. Specifically:

| | What | NuGet |
| --- | --- | --- |
| `src/Packet.Core/` | Shared primitives (Callsign, Ax25Address, KissFrame) | [`Packet.Core`](https://www.nuget.org/packages/Packet.Core) |
| `src/Packet.Ax25/` | AX.25 v2.2 frame codec + connected-mode session machine + `Ax25Listener` | [`Packet.Ax25`](https://www.nuget.org/packages/Packet.Ax25) |
| `src/Packet.Kiss.Abstractions/` | KISS modem interface contract | [`Packet.Kiss.Abstractions`](https://www.nuget.org/packages/Packet.Kiss.Abstractions) |
| `src/Packet.Kiss/` | KISS framing, ACKMODE, multi-drop, TCP transport | [`Packet.Kiss`](https://www.nuget.org/packages/Packet.Kiss) |
| `src/Packet.Aprs/` | APRS frame codec | not yet published |
| `src/Packet.Agw/` | AGW (SV2AGW) client | not yet published |
| `src/Packet.Axudp/` | AXUDP transport | not yet published |
| `src/Packet.Kiss.NinoTnc/` | NinoTNC-specific KISS transport | not yet published |
| `src/Packet.Mcp/` | MCP server scaffolding | not yet published |
| `src/Packet.Rhp2/` + `.Server/` | RHP v2 protocol | not yet published |
| `src/Packet.Node/` + `.Extensions/` | The packet node host (web UI, REST, MCP, plugin shim) | n/a — application |

The dependent SDL state-machine tables come from a sibling repo — see [the sibling repos](#sibling-repos) below.

## Sibling repos

Following the 5-repo split agreed 2026-05-17:

| Repo | What it owns |
| --- | --- |
| **[`m0lte/ax25sdl`](https://github.com/m0lte/ax25sdl)** *(private during prove-out)* | AX.25 v2.2 SDL transcriptions, codegen tools (seven backends), multi-language artefact publishing. Publishes `Packet.Ax25.Sdl` to NuGet, `ax25sdl` to npm. |
| **`m0lte/packet.net`** (this repo) | .NET libraries (`Packet.Core` / `.Ax25` / `.Kiss` / …) + the packet-node host. C# conformance tests + interop CI against LinBPQ / XRouter / rax25 / NinoTNC pair. |
| **[`m0lte/ax25-ts`](https://github.com/m0lte/ax25-ts)** *(public)* | The `@packet-net/ax25` TypeScript library + its examples + TS conformance + TS interop. Extracted from `web/ax25/` here on 2026-05-17 with history preserved; this repo's `interop.yml` clones it for the integration-test step. |
| **[`m0lte/packet-term-tui`](https://github.com/m0lte/packet-term-tui)** *(private)* | `Packet.Term` — the C# Terminal.Gui v2 TUI for AX.25 sessions over a USB KISS modem. Consumes `Packet.Core` / `Packet.Ax25` / `Packet.Kiss` from NuGet. |
| **[`m0lte/packet-term-web`](https://github.com/m0lte/packet-term-web)** *(public)* | The browser TNC2 emulator (single-file HTML demo). Deployed at https://packet-term.m0lte.uk. Consumes `@packet-net/ax25` from npm. |

The `ax25sdl` spec repo is the longest-lived contributor surface; if you want to file a spec-side issue or contribute a new SDL page transcription, that's the home. Tom is working with the original AX.25 authors on whether `packethacking/ax25spec` becomes the canonical community home — `m0lte/ax25sdl` is the prove-out venue.

## Node goals

- Zero file editing for configuration — everything web-based.
- One-line install (`curl … | sudo bash`) plus Docker image.
- Cross-platform: Linux (x64 / arm64 / armhf), Windows x64, macOS arm64 + x64. systemd service on Linux (Debian / Ubuntu / Raspberry Pi OS first class).
- First-rate REST API with modern auth (Argon2id + WebAuthn/passkeys + JWT).
- Built-in MCP server for ops, diagnostics, and network exploration.
- Live packet monitoring + link troubleshooting that go beyond plain frame tracing.
- Continuous interoperability against LinBPQ / XRouter / rax25 / direwolf, with a 72-hour LinBPQ soak running nightly.

## What this is NOT

- A BBS, chat server, mailbox, or DAPPS implementation — those live as out-of-tree plugins.
- An HF waveform stack — talks to KISS modems (over TCP / serial) and AXUDP only. VARA, ARDOP, and friends are out of scope for v1.
- A drop-in LinBPQ replacement — aims for protocol-level interoperability, not bug-for-bug feature parity.

## Roadmap

**[`docs/plan.md`](docs/plan.md) is the authoritative living plan** — direction, status, working agreements, glossary, reference shelf, and amendment log. Read it before contributing.

## Building

```sh
dotnet build
dotnet test --filter "Category!=HardwareLoop&Category!=Interop"
```

Requires the .NET 10 SDK (see `global.json`).

## License

[MIT](LICENSE).

## Acknowledgements

- The [packethacking](https://github.com/packethacking) AX.25 2.2 specification rewrite.
- John Wiseman G8BPQ for LinBPQ, decades of packet work, and the multi-drop KISS / ACKMODE extensions.
- The [Online Amateur Radio Community (OARC)](https://oarc.uk).
