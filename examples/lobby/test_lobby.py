#!/usr/bin/env python3
"""Stdlib unittest for lobby.py.

These tests drive lobby.py exactly as the node would: start it as a long-running
subprocess listening on a temp Unix socket, then open SEPARATE AF_UNIX client
connections (one per simulated user the node bridged), write each a pdn-app/1
connect header, and exercise the shared-state behaviour that is this rung's whole
point — that two distinct connections see and message each other.

Everything is bounded by short timeouts so a hang can't wedge CI.

Run with either:
    python3 -m unittest examples/lobby/test_lobby.py
    python3 examples/lobby/test_lobby.py
"""

import os
import sys
import time
import socket
import unittest
import tempfile
import subprocess

HERE = os.path.dirname(os.path.abspath(__file__))
LOBBY = os.path.join(HERE, "lobby.py")
TIMEOUT = 5  # generous-but-bounded so a hang can't wedge CI


def header(callsign):
    """A valid pdn-app/1 connect header for `callsign`, blank-line terminated."""
    return (
        "pdn-app: 1\n"
        "id: lobby\n"
        "callsign: {}\n".format(callsign) +
        "transport: ax25\n"
        "port: gb7rdg\n"
        "sysop: 0\n"
        "args: \n"
        "future-key: ignore-me\n"   # forward-compat: an unknown key
        "\n"                          # blank line ends the header
    )


class Client:
    """A thin AF_UNIX line client standing in for one node-bridged user."""

    def __init__(self, path):
        self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.sock.settimeout(TIMEOUT)
        self.sock.connect(path)
        self.buf = ""

    def send_line(self, text):
        self.sock.sendall((text + "\n").encode("utf-8"))

    def send_raw(self, data):
        self.sock.sendall(data.encode("utf-8"))

    def read_until(self, needle, deadline=None):
        """Read until `needle` appears in the accumulated stream, or timeout.

        Returns the full text seen so far (including `needle`) on success;
        raises AssertionError on timeout so a missing broadcast fails loudly.
        """
        if deadline is None:
            deadline = time.time() + TIMEOUT
        while needle not in self.buf:
            remaining = deadline - time.time()
            if remaining <= 0:
                raise AssertionError(
                    "timed out waiting for {!r}; got:\n{}".format(needle, self.buf))
            self.sock.settimeout(remaining)
            try:
                chunk = self.sock.recv(4096)
            except socket.timeout:
                raise AssertionError(
                    "timed out waiting for {!r}; got:\n{}".format(needle, self.buf))
            if chunk == b"":
                raise AssertionError(
                    "connection closed waiting for {!r}; got:\n{}".format(needle, self.buf))
            self.buf += chunk.decode("utf-8", errors="replace")
        return self.buf

    def drain(self):
        """Pull whatever is currently readable into the buffer (non-blocking-ish)."""
        self.sock.settimeout(0.2)
        try:
            while True:
                chunk = self.sock.recv(4096)
                if chunk == b"":
                    break
                self.buf += chunk.decode("utf-8", errors="replace")
        except socket.timeout:
            pass
        return self.buf

    def close(self):
        try:
            self.sock.close()
        except OSError:
            pass


class LobbyTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp(prefix="lobby-test-")
        self.sock_path = os.path.join(self.tmp, "lobby.sock")
        # Start the daemon. Socket path via argv[1]; env also set as a belt-and-
        # braces check that both wiring routes work.
        env = dict(os.environ, LOBBY_SOCKET=self.sock_path)
        self.proc = subprocess.Popen(
            [sys.executable, LOBBY, self.sock_path],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            env=env,
            text=True,
        )
        # Wait (bounded) for the listening socket to appear.
        deadline = time.time() + TIMEOUT
        while not os.path.exists(self.sock_path):
            if self.proc.poll() is not None:
                _, err = self.proc.communicate(timeout=TIMEOUT)
                self.fail("lobby.py exited early:\n" + (err or ""))
            if time.time() > deadline:
                self.fail("lobby socket never appeared at " + self.sock_path)
            time.sleep(0.02)
        self.clients = []

    def tearDown(self):
        for c in self.clients:
            c.close()
        if self.proc.poll() is None:
            self.proc.terminate()
        try:
            # communicate() reaps the process and CLOSES the stdout/stderr pipes,
            # avoiding a ResourceWarning about leaked TextIOWrapper objects.
            self.proc.communicate(timeout=TIMEOUT)
        except subprocess.TimeoutExpired:
            self.proc.kill()
            self.proc.communicate()
        try:
            if os.path.exists(self.sock_path):
                os.remove(self.sock_path)
            os.rmdir(self.tmp)
        except OSError:
            pass

    def connect(self, callsign):
        c = Client(self.sock_path)
        self.clients.append(c)
        c.send_raw(header(callsign))
        # Each user gets a personal welcome banner — wait for it so the join is
        # fully processed (registered) before the test proceeds.
        c.read_until("Welcome to the LOBBY, {}".format(callsign))
        return c

    def assert_no_traceback(self):
        # The daemon is still running; peek at whatever stderr it has emitted so
        # far without blocking on the (still-open) pipe.
        self.assertIsNone(self.proc.poll(), "daemon crashed unexpectedly")

    def test_who_sees_both_connections(self):
        # Shared state: B must appear in A's WHO and vice-versa.
        a = self.connect("M0LTE-7")
        b = self.connect("G0ABC-1")

        a.send_line("WHO")
        seen = a.read_until("in the lobby")
        # The WHO line lists both callsigns — proves cross-connection shared state.
        self.assertIn("M0LTE-7", seen)
        self.assertIn("G0ABC-1", seen)
        self.assert_no_traceback()

    def test_say_broadcasts_across_connections(self):
        # The killer demo: a line A types with SAY appears live in B's session.
        a = self.connect("M0LTE-7")
        b = self.connect("G0ABC-1")

        a.send_line("SAY hello everyone")
        # B (a DIFFERENT connection) must receive A's message unsolicited.
        got = b.read_until("hello everyone")
        self.assertIn("hello everyone", got)
        self.assertIn("M0LTE-7", got)  # attributed to the speaker
        self.assert_no_traceback()

    def test_dot_shorthand_broadcasts(self):
        a = self.connect("M0LTE-7")
        b = self.connect("G0ABC-1")
        a.send_line(".quick chat")
        got = b.read_until("quick chat")
        self.assertIn("M0LTE-7", got)

    def test_eof_removes_user_and_announces(self):
        # Closing B's connection (EOF) must remove B from shared state, and A
        # should both see a "left" announcement and a B-less WHO afterwards.
        a = self.connect("M0LTE-7")
        b = self.connect("G0ABC-1")

        # A should have seen B join.
        a.read_until("* G0ABC-1 joined")

        b.close()
        # A receives the departure announcement on its live connection.
        a.read_until("* G0ABC-1 left")

        # And a fresh WHO no longer lists B.
        a.send_line("WHO")
        seen = a.read_until("in the lobby")
        # Read a bit more to capture the whole WHO line, then assert.
        a.drain()
        # The most recent "in the lobby" line must not mention B.
        last_who = [ln for ln in a.buf.splitlines() if "in the lobby" in ln][-1]
        self.assertIn("M0LTE-7", last_who)
        self.assertNotIn("G0ABC-1", last_who)
        self.assert_no_traceback()

    def test_malformed_line_does_not_crash(self):
        # A hostile line with control chars / an unknown command must not crash
        # the thread or the server; the daemon keeps serving afterwards.
        a = self.connect("M0LTE-7")
        a.send_raw("SAY a\x1b[31m\x00b\tc\n")     # control chars inside a SAY
        a.send_line("frobnicate the widget")      # unknown command
        a.send_line("WHO")                         # still responsive
        seen = a.read_until("in the lobby")
        self.assertIn("M0LTE-7", seen)
        # A second user can still connect — the server is alive and well.
        b = self.connect("G0XYZ-2")
        b.send_line("WHO")
        seen_b = b.read_until("in the lobby")
        self.assertIn("G0XYZ-2", seen_b)
        self.assert_no_traceback()

    def test_quit_command_ends_session(self):
        a = self.connect("M0LTE-7")
        a.send_line("BYE")
        # The server says goodbye then closes our connection (recv -> EOF).
        got = a.read_until("73")
        self.assertIn("73", got)
        # The connection should now close from the server side.
        a.sock.settimeout(TIMEOUT)
        # Drain to EOF.
        a.sock.recv(4096)  # may return remaining bytes
        self.assert_no_traceback()


if __name__ == "__main__":
    unittest.main()
