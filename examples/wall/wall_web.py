#!/usr/bin/env python3
"""WALL web view — the *human-plane* half of the reference pdn app.

The packet-plane app (``wall.py``) serves connecting stations over the stdio
session protocol. This is the other plane: a tiny read-only web page of the
same wall, served to ordinary browsers. The two share ONE thing — the on-disk
state file — and nothing else.

This server knows NOTHING about pdn. It does not import pdn code. The ONLY
contract is the HTTP reverse-proxy gateway, which is the whole point: a pdn web
app can be written in any language, in its own process, behind the node's
gateway:

  * pdn reverse-proxies ``GET /apps/wall/<path>`` to this server's upstream
    (e.g. ``http://127.0.0.1:9090/<path>``) with the ``/apps/wall`` prefix
    STRIPPED — so we see ``<path>`` from the site root and MUST emit only
    RELATIVE URLs (the browser is actually under ``/apps/wall/``).
  * We bind LOOPBACK ONLY — only pdn should ever reach us. The port comes from
    ``argv[1]`` (default 9090) or the ``WALL_WEB_PORT`` env var.
  * pdn injects trusted request headers (it strips any client-supplied copy
    first): ``X-Pdn-User`` (viewer callsign, may be empty), ``X-Pdn-Scope``
    (read/operate/admin, may be empty), ``X-Pdn-Gateway: 1``. We read
    ``X-Pdn-User`` to greet the viewer.

The wall is READ-ONLY here in v1 — posting happens over the packet session.

Stdlib only; runs on a stock Debian python3 (3.9+).
"""

import os
import sys
import html
import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HOST = "127.0.0.1"          # loopback only — only pdn's gateway should reach us
DEFAULT_PORT = 9090
WALL_NAME = "pdn WALL"      # shown in the page header
MAX_POSTS = 50              # newest N posts to render


def state_path():
    """Resolve the state file the SAME way wall.py does: WALL_FILE, else wall.txt."""
    return os.environ.get("WALL_FILE") or "wall.txt"


def load_posts():
    """Read all posts from the state file. Returns a list of (ts, who, text).

    Lock-free: read the whole file and parse, matching wall.py's on-disk format
    (one post per line, tab-separated ``ISO-ts \\t callsign \\t text``).
    Malformed/short lines are skipped, not fatal. A missing file is an empty
    wall. This must NEVER raise — the handler relies on it being total.
    """
    posts = []
    try:
        with open(state_path(), "r", encoding="utf-8", errors="replace") as f:
            for line in f:
                line = line.rstrip("\n")
                if not line:
                    continue
                parts = line.split("\t", 2)
                if len(parts) == 3:
                    posts.append((parts[0], parts[1], parts[2]))
    except OSError:
        pass  # missing/unreadable file — just an empty wall, never a 500
    return posts


# Inline CSS only — no external assets, so the page is self-contained and works
# regardless of the proxy path prefix. Mobile-friendly via viewport + max-width.
PAGE_CSS = """
  :root { color-scheme: light dark; }
  * { box-sizing: border-box; }
  body { font: 16px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif;
         margin: 0; background: #f4f5f7; color: #1c1f23; }
  @media (prefers-color-scheme: dark) {
    body { background: #14161a; color: #e6e8eb; }
    .post { background: #1d2026; border-color: #2b2f37; }
    header { border-color: #2b2f37; }
    .meta { color: #8a9099; }
  }
  .wrap { max-width: 720px; margin: 0 auto; padding: 1rem; }
  header { border-bottom: 1px solid #d9dce1; padding-bottom: .75rem;
           margin-bottom: 1rem; }
  h1 { font-size: 1.3rem; margin: 0 0 .25rem; }
  .meta { color: #5a6069; font-size: .85rem; }
  .post { background: #fff; border: 1px solid #e3e6ea; border-radius: 8px;
          padding: .6rem .8rem; margin: .5rem 0; word-wrap: break-word;
          overflow-wrap: anywhere; }
  .post .when { color: #5a6069; font-variant-numeric: tabular-nums;
                font-size: .8rem; }
  .post .who { font-weight: 600; }
  .empty { color: #5a6069; padding: 1.5rem 0; text-align: center; }
  footer { margin-top: 1.5rem; color: #5a6069; font-size: .85rem;
           border-top: 1px solid #d9dce1; padding-top: .75rem; }
"""


def render_post(post):
    """Render one (ts, who, text) tuple as a safe HTML block.

    EVERYTHING here is user-supplied (callsign and text come straight off the
    wire; the timestamp is ours but we escape it anyway for uniformity), so it
    ALL goes through html.escape — never inject raw post text into the page.
    """
    ts, who, text = post
    return (
        '    <div class="post"><span class="when">[{}]</span> '
        '<span class="who">{}</span>: {}</div>'
    ).format(html.escape(ts), html.escape(who), html.escape(text))


def render_page(viewer):
    """Build the full, self-contained HTML page for the current wall state.

    ``viewer`` is the raw X-Pdn-User header value (possibly empty/None).
    """
    posts = load_posts()
    count = len(posts)
    # Newest first: the file is append-ordered (oldest first), so reverse the
    # tail. Clearly ordered for the reader, and bounded to MAX_POSTS.
    recent = list(reversed(posts[-MAX_POSTS:]))

    viewer = (viewer or "").strip()
    if viewer:
        who_line = "viewing as {}".format(html.escape(viewer))
    else:
        who_line = "viewing anonymously"

    if recent:
        body = "\n".join(render_post(p) for p in recent)
    else:
        body = ('    <div class="empty">The wall is empty.<br>'
                "Be the first — see below.</div>")

    plural = "" if count == 1 else "s"
    generated = datetime.datetime.now(datetime.timezone.utc).strftime(
        "%Y-%m-%d %H:%MZ")

    # <base href="./"> belt-and-braces: any relative link resolves under the
    # gateway's /apps/wall/ prefix, never escaping it. (This page has no links,
    # but it documents the rule for app authors who copy this file.)
    return (
        "<!doctype html>\n"
        '<html lang="en">\n'
        "<head>\n"
        '  <meta charset="utf-8">\n'
        '  <meta name="viewport" content="width=device-width, initial-scale=1">\n'
        '  <base href="./">\n'
        "  <title>{name}</title>\n"
        "  <style>{css}</style>\n"
        "</head>\n"
        "<body>\n"
        '  <div class="wrap">\n'
        "    <header>\n"
        "      <h1>{name}</h1>\n"
        '      <div class="meta">{count} post{plural} &middot; {who}</div>\n'
        "    </header>\n"
        "{body}\n"
        "    <footer>Read-only view. "
        "Post by connecting to the node and typing <strong>WALL</strong>."
        "<br>Page generated {generated}.</footer>\n"
        "  </div>\n"
        "</body>\n"
        "</html>\n"
    ).format(
        name=html.escape(WALL_NAME),
        css=PAGE_CSS,
        count=count,
        plural=plural,
        who=who_line,
        body=body,
        generated=generated,
    )


class WallHandler(BaseHTTPRequestHandler):
    """Serve GET / (the wall page); 404 everything else."""

    # The path the browser hits is /apps/wall/ ; pdn strips that prefix, so the
    # ONLY path we ever expect is "/". Anything else is a 404.
    server_version = "wall_web/1"

    def do_GET(self):
        # Strip a query string; we only route on the path component.
        path = self.path.split("?", 1)[0]
        if path == "/":
            self._send_page()
        else:
            self._send_404()

    def do_HEAD(self):
        # Honour HEAD on / so proxies/health checks behave; no body.
        path = self.path.split("?", 1)[0]
        if path == "/":
            self._write_headers(200, len(self._page_bytes()), "text/html; charset=utf-8")
        else:
            self._write_headers(404, 0, "text/plain; charset=utf-8")

    def _page_bytes(self):
        # X-Pdn-User is injected by pdn (client copies are stripped upstream);
        # trust it for the greeting only.
        return render_page(self.headers.get("X-Pdn-User", "")).encode("utf-8")

    def _send_page(self):
        try:
            body = self._page_bytes()
        except Exception:
            # Defensive: render_page should never raise, but if it somehow does
            # we degrade to a minimal 200 rather than leaking a 500/stacktrace.
            body = (b"<!doctype html><meta charset=utf-8>"
                    b"<p>The wall is temporarily unavailable.</p>")
        self._write_headers(200, len(body), "text/html; charset=utf-8")
        self._safe_write(body)

    def _send_404(self):
        body = b"<!doctype html><meta charset=utf-8><p>Not found.</p>"
        self._write_headers(404, len(body), "text/html; charset=utf-8")
        self._safe_write(body)

    def _write_headers(self, status, length, ctype):
        self.send_response(status)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(length))
        # No caching: the wall changes whenever a station posts over packet.
        self.send_header("Cache-Control", "no-store")
        self.end_headers()

    def _safe_write(self, body):
        try:
            self.wfile.write(body)
        except (BrokenPipeError, ConnectionResetError):
            pass  # client/proxy hung up mid-write — nothing to do

    def log_message(self, fmt, *args):
        # Suppress the default per-request stderr spam; pdn does the logging.
        pass


def resolve_port(argv):
    """Port from argv[1], else WALL_WEB_PORT, else DEFAULT_PORT. Bad input → default."""
    raw = None
    if len(argv) > 1:
        raw = argv[1]
    elif os.environ.get("WALL_WEB_PORT"):
        raw = os.environ["WALL_WEB_PORT"]
    if raw is None:
        return DEFAULT_PORT
    try:
        return int(raw)
    except (TypeError, ValueError):
        return DEFAULT_PORT


def main(argv):
    port = resolve_port(argv)
    # ThreadingHTTPServer so a slow client can't block another browser's view.
    server = ThreadingHTTPServer((HOST, port), WallHandler)
    sys.stderr.write("wall_web: serving {} on http://{}:{}/ (wall file: {})\n".format(
        WALL_NAME, HOST, port, state_path()))
    sys.stderr.flush()
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main(sys.argv)
