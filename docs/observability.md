# Observability: the Prometheus `/metrics` exporter

The node exposes a Prometheus-compatible scrape endpoint at **`GET /metrics`** ([#457](https://github.com/packet-net/packet.net/issues/457)) so operators can graph and alert on node health, per-port behaviour, and NET/ROM forwarding throughput with standard tooling (Prometheus + Grafana). It complements — it does not replace — the REST/SSE `/api/v1/*` telemetry the web control panel consumes.

## Where it lives, and the single source of truth

The exporter is `src/Packet.Node/Api/PdnMetricsApi.cs`. Every value it emits is read from the **same live state** that backs the REST/SSE telemetry — there is no second counter store:

- **`NodeTelemetry`** (the frame-trace tap) — per-port frame totals and per-(port, peer) byte / REJ / SREJ rollups. The same source `GET /api/v1/links` and `GET /api/v1/ports` project from.
- **The live `Ax25Session` timer state** — current retry counter (RC), pending I-frame queue depth, and outstanding (sent-but-unacked) I-frames, summed over a port's sessions. The same monitor-v2 source `/api/v1/links` reads `SmoothedRttMs` / `Retries` from.
- **`NetRomService.ForwardingStats`** — the L3 transit-forwarding counters (frames/bytes forwarded + drops by reason), bumped on the same `ForwardDatagram` path that does the routing.
- **`PdnReadApi.BuildStatus` / `BuildPorts`** — node identity, version, uptime, ports up/total, session count, NET/ROM neighbour/destination counts. Literally the same projection helpers the `/api/v1/status` and `/api/v1/ports` endpoints call.

So a number on `/metrics` and the corresponding number in the JSON API can never diverge: they are computed from one set of counters.

## Exposure / auth posture

`/metrics` is mapped on the **same Kestrel listener** as the REST API (simplest; no second port to bind or firewall) and is gated by the **same `read` scope policy** (`PdnAuthPolicies.Read`) as the rest of the read surface. Concretely:

- With `management.auth.enabled` **off** (the default), `/metrics` is **unauthenticated** — and the node **binds 127.0.0.1 by default**, so the out-of-the-box posture is the standard localhost-scrape one (run the Prometheus agent on the same host, or scrape across a Tailscale tailnet).
- With auth **on**, `/metrics` requires a `read`-scoped bearer token, exactly like `/api/v1/status`.

The endpoint is read-only and has no side effects. The response content type is `text/plain; version=0.0.4; charset=utf-8` (the Prometheus text exposition content type).

## Exporter mechanics

In-process, **no Prometheus client dependency**. A hand-rolled `PrometheusTextWriter` (~70 lines, in the same file) emits `# HELP` / `# TYPE` headers and value samples, escaping label values per the exposition format. A new package for a read-only scrape surface wasn't worth the dependency footprint.

## Label cardinality — bounded by design

The **only** label is **`port`** (one value per *configured* port — a closed set the operator controls), plus a 3-value `reason` label on the forward-drops series. **There is deliberately no `peer` / `callsign` label anywhere in `/metrics`.** A per-(port, peer) link is keyed by the *remote* callsign, which is unbounded as random stations are heard on the air — emitting a series per peer would let a busy or hostile channel blow up Prometheus's series count. So per-link counters (bytes, REJ, SREJ, queue depth, retries) are **aggregated up to the port** before export. Per-peer detail stays on the bounded-by-request `GET /api/v1/links` JSON surface, where the client asks for it explicitly.

## The metric set

All metrics use the `pdn_` namespace. Counters are monotonic over the process lifetime; gauges are point-in-time.

### Node health

| Metric | Type | Labels | Meaning |
|---|---|---|---|
| `pdn_build_info` | gauge | `version`, `callsign`, `alias` | Constant `1`; the build/version + node identity live in the labels (the conventional info-gauge pattern). |
| `pdn_uptime_seconds` | gauge | — | Node process uptime in seconds. |
| `pdn_ports_total` | gauge | — | Number of configured radio ports. |
| `pdn_ports_up` | gauge | — | Number of configured ports currently up. |
| `pdn_sessions` | gauge | — | Active connected-mode sessions across all ports. |
| `pdn_netrom_neighbours` | gauge | — | Directly-heard NET/ROM neighbours. |
| `pdn_netrom_destinations` | gauge | — | Known NET/ROM destinations in the routing table. |
| `pdn_process_resident_memory_bytes` | gauge | — | Working-set memory of the node process. |
| `pdn_process_cpu_seconds_total` | counter | — | Total CPU time consumed by the node process. |
| `pdn_process_threads` | gauge | — | OS threads in the node process. |
| `pdn_process_start_time_seconds` | gauge | — | Process start time as Unix epoch seconds. |
| `pdn_dotnet_gc_heap_bytes` | gauge | — | Managed GC heap size. |
| `pdn_traffic_log_dropped_frames_total` | counter | — | Frames the persistent traffic-log writer dropped (writer behind — never the radio path's loss). |

### Per-port / per-link (aggregated to the port)

| Metric | Type | Labels | Meaning |
|---|---|---|---|
| `pdn_port_up` | gauge | `port` | Port up (1) / not up (0). |
| `pdn_port_sessions` | gauge | `port` | Active sessions on the port. |
| `pdn_port_frames_received_total` | counter | `port` | AX.25 frames received on the port. |
| `pdn_port_frames_transmitted_total` | counter | `port` | AX.25 frames transmitted on the port. |
| `pdn_port_info_bytes_received_total` | counter | `port` | Information-field bytes received (summed over peers). |
| `pdn_port_info_bytes_transmitted_total` | counter | `port` | Information-field bytes transmitted (summed over peers). |
| `pdn_port_rej_total` | counter | `port` | REJ (go-back-N reject) frames seen (summed over peers). |
| `pdn_port_srej_total` | counter | `port` | SREJ (selective reject) frames seen (summed over peers). |
| `pdn_port_retries` | gauge | `port` | Sum of the current retry counter (RC) over the port's live sessions. |
| `pdn_port_tx_queue_depth` | gauge | `port` | Sum of pending (unsent) I-frames queued over the port's live sessions. |
| `pdn_port_outstanding_iframes` | gauge | `port` | Sum of sent-but-unacknowledged I-frames over the port's live sessions. |

### Forwarding throughput

| Metric | Type | Labels | Meaning |
|---|---|---|---|
| `pdn_netrom_forwarded_frames_total` | counter | — | NET/ROM transit datagrams forwarded toward their destination. |
| `pdn_netrom_forwarded_bytes_total` | counter | — | NET/ROM transit datagram bytes forwarded. |
| `pdn_netrom_forward_drops_total` | counter | `reason` (`ttl_expired` \| `looped` \| `no_route`) | Transit datagrams dropped on the forward path, by reason. |

The forwarding bucket is all-zero on an endpoint-only or NET/ROM-disabled node (nothing is ever forwarded).

## Example scrape

```sh
curl -s http://127.0.0.1:8080/metrics
```

```
# HELP pdn_uptime_seconds Node process uptime in seconds.
# TYPE pdn_uptime_seconds gauge
pdn_uptime_seconds 3612
# HELP pdn_port_frames_received_total AX.25 frames received on the port.
# TYPE pdn_port_frames_received_total counter
pdn_port_frames_received_total{port="vhf"} 14823
...
```

A minimal Prometheus scrape config:

```yaml
scrape_configs:
  - job_name: pdn
    static_configs:
      - targets: ["127.0.0.1:8080"]
```
