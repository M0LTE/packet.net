# packetnet-tsnet — pdn's embedded Tailscale node

A small, static Go binary that gives a pdn node a real, browser-trusted HTTPS
endpoint with **no public DNS, no port-forward, and no cert management** — so
WebAuthn/passkeys work remotely. It embeds a Tailscale node via
[`tailscale.com/tsnet`](https://pkg.go.dev/tailscale.com/tsnet) (userspace — no
`tailscaled`, no root, no TUN), terminates TLS for `pdn.<tailnet>.ts.net` with
the automatic Let's Encrypt cert, and reverse-proxies to pdn's loopback HTTP.

This is the Go half of the network-access design — see
[`docs/network-access.md`](../../docs/network-access.md), §"The sidecar". pdn's
`TailscaleSidecarHostedService` supervises it: launches it when
`tailscale.enabled`, restarts on failure with backoff, and SIGTERMs it on
shutdown. It is **infrastructure, not an app** — it never appears in the apps
inventory.

## Flags

| Flag             | Meaning                                                                       |
| ---------------- | ----------------------------------------------------------------------------- |
| `--hostname`     | Desired node name → `<hostname>.<tailnet>.ts.net` (the actual name is read back). |
| `--state-dir`    | Persistent tsnet state directory. **Load-bearing** for a stable hostname/cert (and therefore stable passkeys) across restarts. Required. |
| `--target`       | The loopback HTTP `host:port` pdn serves, to reverse-proxy to (e.g. `127.0.0.1:8080`). |
| `--authkey-file` | Optional path to a file holding a tailnet pre-auth key. If the file exists and is non-empty its contents are used as the tsnet `AuthKey` for first join; otherwise the sidecar falls back to interactive login. |
| `--funnel`       | Expose publicly via Tailscale Funnel instead of tailnet-only. Off by default. |

## Status contract (stdout)

`stdout` is a **JSON status stream — one object per line** — that pdn's
supervisor parses. `stderr` is free-form logs (the raw tsnet/`Logf` output).

```json
{"state":"starting"}
{"state":"needs-login","authURL":"https://login.tailscale.com/a/…"}
{"state":"running","fqdn":"pdn.<tailnet>.ts.net"}
{"state":"error","error":"…"}
```

- `starting` — emitted at launch.
- `needs-login` — emitted when interactive auth is required (no/invalid auth
  key); `authURL` is the `login.tailscale.com` URL to surface to the operator.
- `running` — emitted once joined and serving TLS; `fqdn` is the assigned
  MagicDNS name (trailing dot trimmed).
- `error` — emitted on a fatal error before exit.

On **SIGTERM** (or SIGINT) the sidecar shuts down gracefully and exits 0.

## Tags come from the auth key

There is deliberately **no `--tags` flag**. A `tsnet` node inherits its tags
from the pre-auth key it joins with — mint the key with the tags you want
(e.g. `--tags=tag:server`, key-expiry disabled) and drop it in the
`--authkey-file`. The sidecar cannot set tags after the fact.

## What the reverse proxy does

The proxy is a `httputil.NewSingleHostReverseProxy` at the loopback `--target`.
Its Director sets, on every outbound request:

- `X-Forwarded-Proto: https`
- `X-Forwarded-Host: <fqdn>`

(plus the default `X-Forwarded-For`). pdn's loopback-trusted `ForwardedHeaders`
read these so the request appears as HTTPS at the `.ts.net` host — making
`Request.IsHttps` true and the WebAuthn origin `https://…`. That is the whole
point of the sidecar.

## Build

Static, `CGO_ENABLED=0`, cross-compiled per arch (`build-deb.sh` does this for
the target RID and stages the binary at `/usr/lib/packetnet/packetnet-tsnet`):

```sh
# amd64
CGO_ENABLED=0 GOOS=linux GOARCH=amd64        go build -trimpath -ldflags="-s -w" -o packetnet-tsnet .
# arm64
CGO_ENABLED=0 GOOS=linux GOARCH=arm64        go build -trimpath -ldflags="-s -w" -o packetnet-tsnet .
# armhf
CGO_ENABLED=0 GOOS=linux GOARCH=arm GOARM=7  go build -trimpath -ldflags="-s -w" -o packetnet-tsnet .
```
