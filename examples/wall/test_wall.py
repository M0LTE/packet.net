#!/usr/bin/env python3
"""Stdlib unittest for wall.py.

These tests drive wall.py exactly as the node would: launch it as a subprocess
with stdin/stdout pipes, write a pdn-app/1 connect header, feed command lines,
close stdin (EOF), and assert on what came back on stdout. WALL_FILE points at
a per-test temp file so the suite is hermetic and never touches a real wall.

Run with either:
    python3 -m unittest examples/wall/test_wall.py
    python3 examples/wall/test_wall.py
"""

import os
import sys
import unittest
import tempfile
import subprocess

HERE = os.path.dirname(os.path.abspath(__file__))
WALL = os.path.join(HERE, "wall.py")
TIMEOUT = 5  # generous-but-bounded so a hang can't wedge CI


def run_wall(stdin_text, wall_file, args=""):
    """Launch wall.py, feed it a header + script, return (stdout, stderr).

    `stdin_text` is the session traffic that follows the header's blank line.
    Closing the pipe (communicate) signals EOF, so the app exits cleanly.
    """
    header = (
        "pdn-app: 1\n"
        "id: wall\n"
        "callsign: M0LTE-7\n"
        "transport: ax25\n"
        "port: gb7rdg\n"
        "sysop: 0\n"
        "args: {}\n"
        "future-key: ignore-me\n"   # forward-compat: an unknown key
        "\n"                          # blank line ends the header
    ).format(args)

    env = dict(os.environ, WALL_FILE=wall_file)
    proc = subprocess.Popen(
        [sys.executable, WALL],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        cwd=HERE,
        env=env,
        text=True,
    )
    out, err = proc.communicate(header + stdin_text, timeout=TIMEOUT)
    return out, err, proc.returncode


class WallTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp(prefix="wall-test-")
        self.wall_file = os.path.join(self.tmp, "wall.txt")

    def tearDown(self):
        try:
            if os.path.exists(self.wall_file):
                os.remove(self.wall_file)
            os.rmdir(self.tmp)
        except OSError:
            pass

    def test_banner_post_list_unknown_and_clean_exit(self):
        # POST a line, then LIST it back, exercise an unknown command, then BYE.
        script = (
            "POST hello from test\n"
            "frobnicate\n"     # unknown command — must not crash
            "LIST\n"
            "BYE\n"
        )
        out, err, rc = run_wall(script, self.wall_file)

        # Banner appears.
        self.assertIn("pdn WALL", out, msg="banner missing\n" + out)
        # POST was confirmed.
        self.assertIn("posted.", out)
        # Unknown command handled gracefully (no traceback).
        self.assertIn("unknown command", out)
        self.assertNotIn("Traceback", err)
        # LIST shows the post, attributed to the header callsign.
        self.assertIn("M0LTE-7: hello from test", out)
        # Clean goodbye + the process actually exited (communicate returned).
        self.assertIn("73", out)
        self.assertIsNotNone(rc)

    def test_eof_exits_without_quit_command(self):
        # No BYE — just feed one command then EOF. The app must still exit
        # promptly (run_wall's timeout would otherwise fail the test).
        out, err, rc = run_wall("LIST\n", self.wall_file)
        self.assertIn("pdn WALL", out)
        self.assertNotIn("Traceback", err)
        self.assertIsNotNone(rc)

    def test_empty_post_rejected(self):
        out, err, rc = run_wall("POST    \nBYE\n", self.wall_file)
        self.assertIn("nothing to post", out)
        # An empty post must not be written to the wall.
        self.assertFalse(os.path.exists(self.wall_file) and
                         os.path.getsize(self.wall_file) > 0)

    def test_args_request_immediate_read(self):
        # Pre-seed a post, then a fresh connect with args "last 5" should show
        # it in the banner area before any command is typed.
        run_wall("POST seed post\nBYE\n", self.wall_file)
        out, err, rc = run_wall("BYE\n", self.wall_file, args="last 5")
        self.assertIn("M0LTE-7: seed post", out)

    def test_persistence_across_invocations(self):
        # First process posts; a brand-new process must see it (proves the
        # state file persists, which is the whole point of the wall).
        out1, _, _ = run_wall("POST persisted across connects\nBYE\n", self.wall_file)
        self.assertIn("posted.", out1)

        out2, _, _ = run_wall("LIST\nBYE\n", self.wall_file)
        self.assertIn("M0LTE-7: persisted across connects", out2)

    def test_post_with_control_chars_is_sanitised(self):
        # A hostile post with embedded tab/newline/escape must collapse to one
        # safe line — the stored file must still be one line per post.
        out, _, _ = run_wall("POST a\tb\x1b[31m c\nBYE\n", self.wall_file)
        self.assertIn("posted.", out)
        with open(self.wall_file, "r", encoding="utf-8") as f:
            lines = [ln for ln in f.read().splitlines() if ln]
        self.assertEqual(len(lines), 1, "post should be exactly one line")
        # No raw ESC byte should survive into the stored record.
        self.assertNotIn("\x1b", lines[0])


if __name__ == "__main__":
    unittest.main()
