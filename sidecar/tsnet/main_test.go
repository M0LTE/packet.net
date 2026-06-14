package main

import (
	"bufio"
	"bytes"
	"io"
	"net"
	"testing"
	"time"
)

func TestParseForwards_Good(t *testing.T) {
	in := []byte(`[
		{"listen":993,"target":"127.0.0.1:1430","tls":"terminate"},
		{"listen":465,"target":"127.0.0.1:1465","tls":"raw"},
		{"listen":143,"target":"127.0.0.1:1431"}
	]`)
	got, err := parseForwards(in)
	if err != nil {
		t.Fatalf("parseForwards: %v", err)
	}
	if len(got) != 3 {
		t.Fatalf("want 3 forwards, got %d: %+v", len(got), got)
	}
	want := []forward{
		{Listen: 993, Target: "127.0.0.1:1430", TLS: "terminate"},
		{Listen: 465, Target: "127.0.0.1:1465", TLS: "raw"},
		{Listen: 143, Target: "127.0.0.1:1431", TLS: "raw"}, // tls omitted ⇒ defaults to raw
	}
	for i := range want {
		if got[i] != want[i] {
			t.Errorf("forward[%d] = %+v, want %+v", i, got[i], want[i])
		}
	}
}

func TestParseForwards_Malformed(t *testing.T) {
	for _, tc := range []struct {
		name string
		in   string
	}{
		{"not json", `not json at all`},
		{"object not array", `{"listen":993}`},
		{"truncated", `[{"listen":993,`},
	} {
		t.Run(tc.name, func(t *testing.T) {
			if _, err := parseForwards([]byte(tc.in)); err == nil {
				t.Fatalf("want error for %q, got nil", tc.in)
			}
		})
	}
}

func TestParseForwards_Empty(t *testing.T) {
	for _, in := range []string{"", "   ", "\n\t "} {
		got, err := parseForwards([]byte(in))
		if err != nil {
			t.Fatalf("empty input %q: unexpected error %v", in, err)
		}
		if len(got) != 0 {
			t.Fatalf("empty input %q: want 0 forwards, got %d", in, len(got))
		}
	}
}

func TestParseForwards_BadEntriesSkipped(t *testing.T) {
	// A mix of good and bad entries: the document is well-formed, so the bad
	// individual entries are skipped (logged) and the good ones survive.
	in := []byte(`[
		{"listen":993,"target":"127.0.0.1:1430","tls":"terminate"},
		{"listen":0,"target":"127.0.0.1:1","tls":"raw"},
		{"listen":70000,"target":"127.0.0.1:2","tls":"raw"},
		{"listen":465,"target":"","tls":"raw"},
		{"listen":25,"target":"127.0.0.1:3","tls":"bogus"},
		{"listen":143,"target":"127.0.0.1:1431","tls":"raw"}
	]`)
	got, err := parseForwards(in)
	if err != nil {
		t.Fatalf("parseForwards: %v", err)
	}
	if len(got) != 2 {
		t.Fatalf("want 2 surviving forwards, got %d: %+v", len(got), got)
	}
	if got[0].Listen != 993 || got[1].Listen != 143 {
		t.Errorf("surviving forwards = %+v, want listen 993 then 143", got)
	}
}

// echoServer is a stand-in for a loopback app target: it accepts one
// connection and echoes everything back. Returns its listen address.
func echoServer(t *testing.T) (addr string, stop func()) {
	t.Helper()
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("echo listen: %v", err)
	}
	go func() {
		for {
			c, err := ln.Accept()
			if err != nil {
				return
			}
			go func(c net.Conn) {
				defer c.Close()
				_, _ = io.Copy(c, c)
			}(c)
		}
	}()
	return ln.Addr().String(), func() { _ = ln.Close() }
}

// TestServeForward_RoundTrip exercises the dial+copy byte-pump without tsnet:
// a plain net.Listener stands in for the tsnet listener (the only thing tsnet
// changes is *which* listener is created — terminate vs raw — so this covers
// both byte-pump paths) and a local echo server stands in for the loopback app.
func TestServeForward_RoundTrip(t *testing.T) {
	target, stopEcho := echoServer(t)
	defer stopEcho()

	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("forward listen: %v", err)
	}
	defer ln.Close()
	go serveForward(ln, target)

	conn, err := net.Dial("tcp", ln.Addr().String())
	if err != nil {
		t.Fatalf("dial forward: %v", err)
	}
	defer conn.Close()
	_ = conn.SetDeadline(time.Now().Add(5 * time.Second))

	want := []byte("hello over the forward\n")
	if _, err := conn.Write(want); err != nil {
		t.Fatalf("write: %v", err)
	}
	got, err := bufio.NewReader(conn).ReadBytes('\n')
	if err != nil {
		t.Fatalf("read: %v", err)
	}
	if !bytes.Equal(got, want) {
		t.Fatalf("round-trip mismatch: got %q, want %q", got, want)
	}
}

// TestServeForward_DialFailureSurvives confirms a connection whose target
// can't be dialled is closed without taking the listener down: a second
// connection to a now-good target still round-trips.
func TestServeForward_DialFailureSurvives(t *testing.T) {
	// Reserve a port, then close it so dialling it fails (connection refused).
	dead, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("reserve: %v", err)
	}
	deadAddr := dead.Addr().String()
	_ = dead.Close()

	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("forward listen: %v", err)
	}
	defer ln.Close()
	go serveForward(ln, deadAddr)

	// First conn: target is dead → pipeConn logs + closes it, listener lives.
	c1, err := net.Dial("tcp", ln.Addr().String())
	if err != nil {
		t.Fatalf("dial 1: %v", err)
	}
	_ = c1.SetReadDeadline(time.Now().Add(5 * time.Second))
	// The forward closes our conn after the failed dial → read returns EOF.
	if _, err := io.ReadAll(c1); err != nil {
		t.Fatalf("read 1: want EOF (nil err from ReadAll), got %v", err)
	}
	c1.Close()

	// Listener must still accept a new connection.
	c2, err := net.Dial("tcp", ln.Addr().String())
	if err != nil {
		t.Fatalf("dial 2 (listener died after dial failure): %v", err)
	}
	c2.Close()
}

// TestServeForward_ListenerCloseStops confirms serveForward returns cleanly
// when its listener is closed (the SIGTERM/ctx-cancel teardown path).
func TestServeForward_ListenerCloseStops(t *testing.T) {
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("listen: %v", err)
	}
	done := make(chan struct{})
	go func() {
		serveForward(ln, "127.0.0.1:1")
		close(done)
	}()
	_ = ln.Close()
	select {
	case <-done:
	case <-time.After(5 * time.Second):
		t.Fatal("serveForward did not return after listener close")
	}
}
