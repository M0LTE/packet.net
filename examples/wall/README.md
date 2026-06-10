# WALL — the reference packet.net app

WALL is a shared message wall (the classic ham-node "wall"/guestbook): a connecting user reads recent posts and can add one of their own. Posts persist to a file, so they survive across connects and are visible to everyone else who connects to the node.

It exists as the **reference "hello-world" application** for the packet.net (pdn) node's app-extensibility platform. The point isn't the wall — it's the *seam*. WALL is a standalone **Python 3 program (stdlib only)** that talks to the node over a tiny stdio protocol and knows **nothing** about the node's .NET internals. The only thing connecting them is the wire protocol below. That language/process boundary is the whole demonstration: a pdn app can be written in any language, in its own process, fully arm's-length from the node — the node is just a broker that pipes a session to it. See [`docs/app-extensibility.md`](../../docs/app-extensibility.md) for the platform design (WALL is the "local-session seam, floor rung" worked example).

## The wire it speaks — `pdn-app/1`

When a user launches the app, the node spawns `wall.py` with stdin/stdout/stderr redirected and speaks `pdn-app/1`. The full contract is summarised in the module docstring at the top of [`wall.py`](wall.py); in brief:

- **Connect header (node → app STDIN, first).** A sequence of `Key: Value` lines (UTF-8, each `\n`-terminated), ended by **one blank line**. Keys: `pdn-app` (wire version `1`), `id` (the launched app id), `callsign` (the connecting station, opaque text e.g. `M0LTE-7`), `transport` (`ax25`|`netrom`|`telnet`), `port` (arrival port id or `-`), `sysop` (`0`/`1`), `args` (tokens typed after the launch verb, space-joined, may be empty). **Unknown keys must be ignored** — new keys will appear over time, and ignoring them is how an app stays forward-compatible.
- **Session traffic (after the blank line).** Each line the user sends arrives on STDIN as one `\n`-terminated UTF-8 line — read it with an ordinary line read. The app replies on STDOUT using `\n` line endings **only**; the node translates `\n` to the transport's newline, so the app must **never emit `\r`**. STDOUT is a pipe, so the app **flushes after every write** (an unflushed prompt looks like a hang). STDERR goes to the node log — diagnostics only, never user-facing.
- **Lifecycle.** STDIN **EOF means "the user is gone"** — exit cleanly. A quit command (`BYE`/`QUIT`) also exits cleanly and returns the user to the node prompt. The exit code is ignored.

That's the entire contract. Note how little of `wall.py` is protocol plumbing (`read_header`, `out`, the read loop) versus app logic — that ratio is the selling point.

## Commands

All commands are case-insensitive and accept the abbreviations shown:

- `LIST [n]` / `READ [n]` (`L`/`R`) — show the last `n` posts (default 10, capped at 50), oldest-of-the-window first, newest at the bottom.
- `POST <text>` / `W <text>` (`P`) — add a post attributed to your connect callsign. Empty text is rejected; text is bounded to 200 chars and sanitised (control chars stripped, whitespace collapsed) so a hostile line can't inject a newline or a terminal escape.
- `HELP` / `?` (`H`) — the command list.
- `BYE` / `B` / `QUIT` / `Q` — short goodbye, then exit back to the node.
- empty line — just reprompts.
- anything else — a friendly "unknown, type ? for help" and reprompts.

If `args` in the connect header requests a read (e.g. `last 5`, or a bare number), WALL shows that many posts immediately after the banner — so a scripted `C WALL last 5` just works.

## State file

Posts are stored one-per-line in a plain-text file. The path is resolved as: the **`WALL_FILE` environment variable** if set, otherwise **`wall.txt` in the current working directory**. The on-disk format is one tab-separated record per line:

```
2026-06-10 18:39Z<TAB>M0LTE-7<TAB>hello world
```

i.e. `ISO-8601-UTC-timestamp \t callsign \t text`. Because the node spawns **one process per connect**, several WALL instances can write the same file at once; each append takes an exclusive `fcntl.flock` so concurrent posts produce whole lines rather than spliced/corrupt ones. Reads are lock-free (read the whole file, take the tail). If the file is unreadable or unwritable, WALL degrades gracefully — it tells the user and logs the detail to STDERR rather than crashing.

## Run it standalone (for testing)

WALL needs no node to run — just pipe a connect header plus some command lines into it. Copy-paste:

```sh
WALL_FILE=/tmp/wall.txt printf 'pdn-app: 1\nid: wall\ncallsign: M0LTE-7\ntransport: ax25\nport: gb7rdg\nsysop: 0\nargs: \n\nPOST hello world\nLIST\nBYE\n' | WALL_FILE=/tmp/wall.txt python3 wall.py
```

Expected output (timestamp will vary):

```
== pdn WALL ==  0 posts on the wall.
Type ? for help, BYE to leave.
wall>
posted.
wall>
[2026-06-10 18:39Z] M0LTE-7: hello world
wall>
73 — posts saved. Bye!
```

(The blank line after each `wall>` is the prompt on its own line — over a real packet session it precedes the user's typed line.)

## Tests

`test_wall.py` is a stdlib `unittest` suite that drives `wall.py` as a subprocess exactly as the node would (writes a header, feeds commands, closes stdin), with `WALL_FILE` pointed at a temp file. It asserts the banner appears, a `POST` then `LIST` round-trips attributed to the header callsign, an unknown command doesn't crash, EOF/`BYE` exits promptly, and that posts persist across separate invocations. Run either:

```sh
python3 -m unittest examples/wall/test_wall.py
# or, from this directory:
python3 test_wall.py
```

## Register it on a node

Add WALL to the node's `applications:` config block. The node matches the `match` verb/alias (here `WALL`, so a user connects with `C WALL [args]`), spawns the `command` with `args`, runs it from `workingDirectory`, and grants the declared `capabilities` (here just `session` — WALL needs no network or config access):

```yaml
applications:
  - id: wall
    match: WALL
    enabled: true
    kind: process
    command: /usr/bin/python3
    args: [ /usr/share/packetnet/apps/wall/wall.py ]
    workingDirectory: /var/lib/packetnet/apps/wall
    capabilities: [ session ]
```

The wall file lands in `workingDirectory` as `wall.txt` by default; set the **`WALL_FILE`** environment variable (e.g. via the process environment) to put it elsewhere — point several `match` aliases at the same `WALL_FILE` and they share one wall, or give each its own file for separate walls.
