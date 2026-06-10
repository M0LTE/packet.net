#!/usr/bin/env python3
"""WALL — the reference "hello-world" app for the packet.net (pdn) node.

A shared message wall (the classic ham-node guestbook): a connecting user reads
recent posts and can add one. Posts persist to a state file, so they survive
across connects and are visible to every other user of the node.

This program knows NOTHING about the node's internals. The ONLY contract is the
``pdn-app/1`` stdio wire protocol, which is the whole point — it proves a node
app can be written in any language, fully separated from the node:

  (a) Connect header  — the node writes ``Key: Value`` lines (UTF-8, ``\\n``
      terminated) to our STDIN, ended by ONE blank line. Unknown keys MUST be
      ignored (forward-compat). Keys: pdn-app, id, callsign, transport, port,
      sysop, args.
  (b) Session traffic — after the blank line: each user line arrives on STDIN
      as one ``\\n``-terminated UTF-8 line; we reply on STDOUT with ``\\n``
      line endings ONLY (never ``\\r`` — the node maps ``\\n`` to the
      transport's newline) and flush after every write. STDERR is the node log.
  (c) Lifecycle      — STDIN EOF means "the user is gone": exit cleanly. A quit
      command also exits cleanly. The exit code is ignored.

Stdlib only; runs on a stock Debian python3 (3.9+).
"""

import os
import re
import sys
import fcntl
import datetime

WIRE_VERSION = "1"          # the pdn-app wire version we speak
MAX_POST_LEN = 200          # bound a single post's text length
MAX_LIST = 50               # cap how many posts a LIST/READ may show
DEFAULT_LIST = 10           # default count for a bare LIST/READ
PROMPT = "wall> "

# Collapse any run of whitespace (incl. would-be newlines/tabs/control chars)
# to a single space, so a hostile line can never inject a newline or an escape
# sequence into the stored file or another reader's terminal.
_WS = re.compile(r"\s+")
_CTRL = re.compile(r"[\x00-\x1f\x7f]")


def state_path():
    """Resolve the state file: WALL_FILE if set, else wall.txt in the cwd."""
    return os.environ.get("WALL_FILE") or "wall.txt"


def sanitise(text):
    """Make arbitrary user text safe to store as exactly one line.

    Strips control chars, collapses whitespace, trims, and bounds the length.
    """
    text = _CTRL.sub(" ", text)
    text = _WS.sub(" ", text).strip()
    return text[:MAX_POST_LEN]


def now_stamp():
    """Current UTC time as an ISO-8601 'YYYY-MM-DD HH:MMZ' string."""
    return datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%d %H:%MZ")


def load_posts():
    """Read all posts from the state file. Returns a list of (ts, who, text).

    Lock-free: read the whole file and parse. Malformed/short lines are skipped
    rather than crashing. A missing file is simply an empty wall.
    """
    posts = []
    path = state_path()
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as f:
            for line in f:
                line = line.rstrip("\n")
                if not line:
                    continue
                parts = line.split("\t", 2)
                if len(parts) == 3:
                    posts.append((parts[0], parts[1], parts[2]))
    except FileNotFoundError:
        pass  # no wall yet — that's fine
    return posts


def append_post(who, text):
    """Append one post under an exclusive lock so concurrent connects (the node
    spawns one process per connect) can't interleave or corrupt a line.

    On-disk format is one post per line: ``ISO-ts \\t callsign \\t text``.
    Raises OSError on an unwritable file; the caller degrades gracefully.
    """
    record = "{}\t{}\t{}\n".format(now_stamp(), sanitise(who) or "?", text)
    path = state_path()
    # Append mode keeps each writer's bytes at the end; flock serialises the
    # write so two simultaneous posts produce two whole lines, never a splice.
    with open(path, "a", encoding="utf-8") as f:
        fcntl.flock(f.fileno(), fcntl.LOCK_EX)
        try:
            f.write(record)
            f.flush()
            os.fsync(f.fileno())
        finally:
            fcntl.flock(f.fileno(), fcntl.LOCK_UN)


def out(line=""):
    """Write one line to the node (STDOUT) with a single \\n and flush.

    Flushing matters: STDOUT is a pipe, not a tty, so an unflushed prompt would
    look like a hang to the connected user.
    """
    sys.stdout.write(line + "\n")
    sys.stdout.flush()


def log(msg):
    """Diagnostics to STDERR — goes to the node log, never to the user."""
    sys.stderr.write("wall: " + msg + "\n")
    sys.stderr.flush()


def read_header():
    """Read the connect header up to the first blank line; return a dict.

    Unrecognised keys are kept too (harmless), but callers only read the ones
    they know — that is the forward-compat rule. Returns {} on immediate EOF.
    """
    header = {}
    for raw in sys.stdin:
        line = raw.rstrip("\n")
        if line == "":
            break  # blank line ends the header
        if ":" in line:
            key, _, value = line.partition(":")
            header[key.strip().lower()] = value.strip()
        # lines without a colon are ignored defensively
    return header


def render(post):
    """Format one (ts, who, text) tuple for display."""
    ts, who, text = post
    return "[{}] {}: {}".format(ts, who, text)


def show_last(n):
    """Print the last n posts (most recent at the bottom), or a friendly note."""
    n = max(1, min(n, MAX_LIST))
    posts = load_posts()
    if not posts:
        out("(the wall is empty — be the first to POST)")
        return
    for post in posts[-n:]:
        out(render(post))


def parse_count(arg, default):
    """Parse an optional integer count argument, falling back to default."""
    arg = arg.strip()
    if not arg:
        return default
    try:
        return int(arg)
    except ValueError:
        return default


def print_help():
    out("commands (case-insensitive):")
    out("  LIST [n] / READ [n]  show the last n posts (default {}, max {})".format(
        DEFAULT_LIST, MAX_LIST))
    out("  POST <text> / W <text>  add a post to the wall")
    out("  HELP / ?             this help")
    out("  BYE / B / QUIT / Q   leave the wall")


def handle(line, callsign):
    """Handle one user command line. Returns True to keep going, False to quit."""
    stripped = line.strip()
    if not stripped:
        return True  # empty line — just reprompt

    word, _, rest = stripped.partition(" ")
    cmd = word.lower()

    if cmd in ("list", "read", "l", "r"):
        show_last(parse_count(rest, DEFAULT_LIST))
    elif cmd in ("post", "w", "p"):
        text = sanitise(rest)
        if not text:
            out("nothing to post — usage: POST <text>")
        else:
            try:
                append_post(callsign, text)
                out("posted.")
            except OSError as e:
                # State file unwritable: tell the user, log the detail, carry on.
                out("sorry — couldn't save your post right now.")
                log("append failed: {}".format(e))
    elif cmd in ("help", "?", "h"):
        print_help()
    elif cmd in ("bye", "b", "quit", "q", "exit"):
        out("73 — posts saved. Bye!")
        return False
    else:
        out('unknown command "{}" — type ? for help'.format(word))
    return True


def main():
    header = read_header()
    if header.get("pdn-app") not in (None, WIRE_VERSION):
        # We only speak v1. Warn (to the log) but keep serving — the line rules
        # are stable, so a higher minor wire is very likely still usable.
        log("unexpected wire version {!r}; proceeding as v1".format(header.get("pdn-app")))

    callsign = header.get("callsign") or "anon"

    # Banner: name + how many posts are on the wall + a one-line command hint.
    try:
        count = len(load_posts())
    except OSError as e:
        count = 0
        log("could not read state file: {}".format(e))
    out("== pdn WALL ==  {} post{} on the wall.".format(count, "" if count == 1 else "s"))
    out("Type ? for help, BYE to leave.")

    # If the launch args asked for a read (e.g. "last 5" or a bare number),
    # honour it immediately so a scripted "C WALL last 5" just works.
    args = (header.get("args") or "").strip()
    if args:
        first, _, rest = args.partition(" ")
        if first.lower() in ("last", "list", "read"):
            show_last(parse_count(rest, DEFAULT_LIST))
        elif first.isdigit():
            show_last(parse_count(first, DEFAULT_LIST))

    # Command loop. An ordinary line read over STDIN; EOF (user gone) ends it.
    while True:
        out(PROMPT.rstrip("\n"))  # prompt is its own line (transport-friendly)
        line = sys.stdin.readline()
        if line == "":
            break  # EOF — the node closed our stdin; the user is gone
        if not handle(line.rstrip("\n"), callsign):
            break


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        pass
    except BrokenPipeError:
        # The node closed the far end mid-write; nothing more we can do.
        pass
    except Exception as e:  # last-ditch: never crash noisily at the user
        log("fatal: {}".format(e))
        sys.exit(0)
