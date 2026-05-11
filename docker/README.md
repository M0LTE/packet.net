# Interop Docker stack

This directory holds the docker-compose stack used by `tests/Packet.Interop.Tests`
and by the `interop` GitHub Actions workflow. It also doubles as a useful local
dev environment for end-to-end work.

## Quick start

```sh
docker compose -f docker/compose.interop.yml up -d --wait
```

Once everything is healthy:

| Service | Host port | Purpose |
|---|---|---|
| LinBPQ web UI | http://localhost:8008 | admin/admin |
| LinBPQ telnet | localhost:8010 | node prompt (user/pass) |
| LinBPQ AGW | localhost:8000 | external app socket |
| LinBPQ KISS-TCP | localhost:8105 | KISS port 1 |
| LinBPQ AXUDP | localhost:8093/udp | AXUDP port 2 |
| Xrouter telnet | localhost:8023 | node prompt |
| Xrouter JSON API | http://localhost:8086 | `/api/v1/*` |
| Xrouter AXUDP | localhost:8095/udp | AXUDP listener |
| net-sim web UI | http://localhost:8080 | topology + start/stop |
| net-sim KISS A | localhost:8100 | afsk1200 town channel |
| net-sim KISS B | localhost:8101 | gfsk9600 backbone |

Tear down:

```sh
docker compose -f docker/compose.interop.yml down -v
```

## Pinning

The image tags here are floating (`latest` / `main`) for dev convenience. CI
pins them via env vars set in `.github/workflows/interop.yml`. Before tagging
a v1 release we will pin the tags here too.

## Files

```
docker/
  compose.interop.yml      stack definition
  linbpq/
    bpq32.cfg              minimal LinBPQ config
  xrouter/
    XROUTER.CFG            minimal XRouter config
  netsim/
    network.yaml           two-node topology for interop tests
```
