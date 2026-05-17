# `@packet-net/ax25` — developer documentation

Documentation for the browser-targeted TypeScript AX.25 library that lives at [`web/ax25/`](../../web/ax25/).

## Quick links

- **Library README** — [`web/ax25/README.md`](../../web/ax25/README.md). The 30-second elevator pitch + scope table.
- **API reference** — [`api/`](api/). Generated from JSDoc by [TypeDoc](https://typedoc.org/) — regenerate with `npm run docs` from `web/ax25/`.
- **Worked examples** — [`web/ax25/examples/`](../../web/ax25/examples/). Self-contained TypeScript files (Web Serial, Node TCP, in-memory mock) that all typecheck against the public API surface.
- **Changelog** — [`web/ax25/CHANGELOG.md`](../../web/ax25/CHANGELOG.md). Keep-a-Changelog format.

> Publishing this library is on hold pending its extraction to a dedicated repo (planned `m0lte/ax25-ts`). The previous packet.net-based publish workflow was removed when the SDL codegen moved out to [`m0lte/ax25sdl`](https://github.com/m0lte/ax25sdl); `@packet-net/ax25@0.2.1` is the last release shipped from this repo.

## API reference entry points

| Module | Use it for |
| --- | --- |
| [`index`](api/index/README.md) | The main `@packet-net/ax25` entry: `Ax25Stack`, `Ax25Session`, `Callsign`, `Ax25Frame`, frame factories, KISS framing, `WebSerialKissTransport`, `Ax25Transport` interface. |
| [`tcp-transport`](api/tcp-transport/README.md) | The `@packet-net/ax25/tcp-transport` subpath: `TcpKissTransport` for Node.js (uses `node:net`). |

## Worked examples

| Example | What it shows |
| --- | --- |
| [`quick-start.ts`](../../web/ax25/examples/quick-start.ts) | Web Serial — open a USB modem, connect to a remote callsign, write/read, disconnect. The canonical browser-side shape. |
| [`node-tcp.ts`](../../web/ax25/examples/node-tcp.ts) | Node TCP — same shape, but via `TcpKissTransport` dialling a local KISS-TCP listener (e.g. our `docker compose -f docker/compose.interop.yml up` net-sim on port 8100). |
| [`with-mock-transport.ts`](../../web/ax25/examples/with-mock-transport.ts) | In-memory mock — wire a `MockTransport` pair, drive a fake peer that replies UA, assert on the round-trip. Pattern for unit-testing user code that consumes `Ax25Stack`. |

All three examples typecheck via `npm run typecheck:examples` from `web/ax25/`. CI runs this on every push.

## Other documentation in this repo

This doc tree is the TypeScript-library-specific subset. The wider repo's documentation lives at:

- [`docs/plan.md`](../plan.md) — the living source of truth for Packet.NET overall.
- [`docs/adr/`](../adr/) — architecture decision records.
- [`docs/sdl-primer.md`](../sdl-primer.md) — SDL shape reference (mandatory before touching `spec-sdl/`).
- [`docs/sdl-verb-catalogue.md`](../sdl-verb-catalogue.md) — how the SDL action verbs map to runtime dispatch.
- [`src/Packet.Ax25/Session/`](../../src/Packet.Ax25/Session/) — the canonical C# implementation of the AX.25 v2.2 session machine. The TS library walks the same SDL tables; the C# code implements more of the figc4.7 subroutines and is the reference for the recovery-path follow-up work.

## Regenerating the API reference

```sh
cd web/ax25
npm install               # one-time, picks up TypeDoc + plugin from devDependencies
npm run docs              # writes into ../../docs/web-ax25/api/
```

The output is checked into git so GitHub renders it for anyone browsing the repo. Re-run after any change to the public API surface (or any JSDoc comment under `web/ax25/src/` outside the `sdl/` subdir, which is excluded from the TypeDoc entry-point set).
