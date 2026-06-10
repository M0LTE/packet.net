#!/usr/bin/env python3
"""Stdlib unittest for wall_web.py.

Starts wall_web.py as a subprocess on an ephemeral loopback port (with
WALL_FILE pointed at a temp file), then drives it with http.client. Everything
is bounded with short timeouts so a hang fails fast rather than wedging CI.
"""

import os
import sys
import time
import socket
import tempfile
import unittest
import subprocess
import http.client

HERE = os.path.dirname(os.path.abspath(__file__))
WALL_WEB = os.path.join(HERE, "wall_web.py")
TIMEOUT = 5.0  # seconds — every network/spawn wait is bounded by this


def free_port():
    """Grab an unused loopback port, then release it for the child to bind."""
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


class WallWebTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.NamedTemporaryFile(
            mode="w", suffix=".txt", delete=False, encoding="utf-8")
        self.wall_file = self.tmp.name
        self.tmp.close()
        self.proc = None

    def tearDown(self):
        if self.proc and self.proc.poll() is None:
            self.proc.terminate()
            try:
                self.proc.wait(timeout=TIMEOUT)
            except subprocess.TimeoutExpired:
                self.proc.kill()
        try:
            os.unlink(self.wall_file)
        except OSError:
            pass

    def _write_wall(self, lines):
        """Write tab-separated post records to the temp wall file."""
        with open(self.wall_file, "w", encoding="utf-8") as f:
            for ts, who, text in lines:
                f.write("{}\t{}\t{}\n".format(ts, who, text))

    def _start_server(self):
        """Spawn wall_web.py and wait until its port answers (bounded)."""
        port = free_port()
        env = dict(os.environ)
        env["WALL_FILE"] = self.wall_file
        self.proc = subprocess.Popen(
            [sys.executable, WALL_WEB, str(port)],
            env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        deadline = time.time() + TIMEOUT
        while time.time() < deadline:
            if self.proc.poll() is not None:
                self.fail("wall_web.py exited early (code {})".format(
                    self.proc.returncode))
            try:
                with socket.create_connection(("127.0.0.1", port), timeout=0.5):
                    return port
            except OSError:
                time.sleep(0.05)
        self.fail("wall_web.py did not start listening within {}s".format(TIMEOUT))

    def _get(self, port, path="/", headers=None):
        """Issue one GET and return (status, body_text)."""
        conn = http.client.HTTPConnection("127.0.0.1", port, timeout=TIMEOUT)
        try:
            conn.request("GET", path, headers=headers or {})
            resp = conn.getresponse()
            return resp.status, resp.read().decode("utf-8", "replace")
        finally:
            conn.close()

    def test_renders_post_text_and_callsign(self):
        self._write_wall([("2026-06-10 14:03Z", "M0LTE-7", "hello world")])
        port = self._start_server()
        status, body = self._get(port)
        self.assertEqual(status, 200)
        self.assertIn("hello world", body)
        self.assertIn("M0LTE-7", body)

    def test_html_escaping(self):
        self._write_wall([("2026-06-10 14:03Z", "G0ABC", "<script>alert(1)</script>")])
        port = self._start_server()
        status, body = self._get(port)
        self.assertEqual(status, 200)
        self.assertIn("&lt;script&gt;", body)
        self.assertNotIn("<script>alert(1)</script>", body)

    def test_viewer_header_reflected(self):
        self._write_wall([("2026-06-10 14:03Z", "M0LTE-7", "hi")])
        port = self._start_server()
        # With X-Pdn-User the callsign appears in the greeting.
        status, body = self._get(port, headers={"X-Pdn-User": "M0LTE-7"})
        self.assertEqual(status, 200)
        self.assertIn("M0LTE-7", body)
        self.assertIn("viewing as", body)
        # Without it, anonymous greeting.
        status, body = self._get(port)
        self.assertEqual(status, 200)
        self.assertIn("viewing anonymously", body)

    def test_empty_wall_friendly_state(self):
        # Temp file exists but is empty — should render the empty state, no crash.
        self._write_wall([])
        port = self._start_server()
        status, body = self._get(port)
        self.assertEqual(status, 200)
        self.assertIn("empty", body.lower())

    def test_missing_wall_file(self):
        # Point at a path that does not exist: still a friendly 200 empty wall.
        os.unlink(self.wall_file)
        port = self._start_server()
        status, body = self._get(port)
        self.assertEqual(status, 200)
        self.assertIn("empty", body.lower())

    def test_other_paths_404(self):
        self._write_wall([])
        port = self._start_server()
        status, _ = self._get(port, path="/nope")
        self.assertEqual(status, 404)


if __name__ == "__main__":
    unittest.main()
