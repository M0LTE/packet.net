// Command packetnet-tsnet is pdn's embedded Tailscale node.
//
// It joins the operator's tailnet via tailscale.com/tsnet (userspace — no
// tailscaled, no root, no TUN), terminates TLS for pdn.<tailnet>.ts.net with
// the auto Let's Encrypt cert, and reverse-proxies to pdn's loopback HTTP. The
// loopback hop carries X-Forwarded-Proto: https + X-Forwarded-Host so pdn's
// loopback-trusted ForwardedHeaders see the request as HTTPS at the .ts.net
// host — which is what makes WebAuthn/passkeys work remotely.
//
// stdout is a JSON status stream (one object per line) the .NET supervisor
// parses; stderr is free-form logs. SIGTERM → graceful shutdown, exit 0.
//
// Tags are NOT a flag here: a tsnet node inherits its tags from the pre-auth
// key it joins with. Mint the key with the desired tags (e.g. tag:server).
package main

import (
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/http/httputil"
	"net/url"
	"os"
	"os/signal"
	"strings"
	"sync"
	"syscall"
	"time"

	"tailscale.com/ipn"
	"tailscale.com/tsnet"
)

func main() {
	hostname := flag.String("hostname", "pdn", "desired node name → <hostname>.<tailnet>.ts.net")
	stateDir := flag.String("state-dir", "", "persistent tsnet state directory (load-bearing for stable hostname/cert)")
	target := flag.String("target", "127.0.0.1:8080", "loopback HTTP host:port to reverse-proxy to")
	authKeyFile := flag.String("authkey-file", "", "optional path to a file holding a tailnet pre-auth key (first-join only)")
	funnel := flag.Bool("funnel", false, "expose publicly via Tailscale Funnel instead of tailnet-only")
	forwardsFile := flag.String("forwards-file", "", "optional path to a JSON array of app-declared port forwards")
	flag.Parse()

	if *stateDir == "" {
		emit(status{State: "error", Error: "--state-dir is required"})
		os.Exit(1)
	}

	// SIGTERM/SIGINT → cancel ctx → graceful shutdown, exit 0.
	ctx, stop := signal.NotifyContext(context.Background(), syscall.SIGTERM, syscall.SIGINT)
	defer stop()

	if err := run(ctx, *hostname, *stateDir, *target, *authKeyFile, *forwardsFile, *funnel); err != nil {
		// A cancelled context is the clean SIGTERM path, not a fatal error.
		if ctx.Err() != nil {
			os.Exit(0)
		}
		emit(status{State: "error", Error: err.Error()})
		os.Exit(1)
	}
}

func run(ctx context.Context, hostname, stateDir, target, authKeyFile, forwardsFile string, funnel bool) error {
	emit(status{State: "starting"})

	authKey := readAuthKey(authKeyFile) // "" if absent/empty — falls back to interactive login.

	srv := &tsnet.Server{
		Hostname: hostname,
		Dir:      stateDir,
		AuthKey:  authKey,
		Logf:     func(format string, args ...any) { fmt.Fprintf(os.Stderr, format+"\n", args...) },
	}
	defer srv.Close()

	// Up blocks until the node is running (or ctx is cancelled). We watch the
	// IPN bus concurrently so we can surface the interactive login URL while Up
	// is still waiting for the operator to authenticate.
	watchCtx, cancelWatch := context.WithCancel(ctx)
	defer cancelWatch()
	go watchLogin(watchCtx, srv)

	if _, err := srv.Up(ctx); err != nil {
		return fmt.Errorf("tsnet up: %w", err)
	}
	cancelWatch() // joined — stop nagging about login.

	fqdn, err := waitForFQDN(ctx, srv)
	if err != nil {
		return err
	}

	// App-declared port forwards run alongside the web reverse-proxy on the
	// same tsnet node. They are best-effort: a missing/garbled forwards file
	// or a single bad entry never blocks the web path.
	startForwards(ctx, srv, forwardsFile)

	proxy := newProxy(target, fqdn)

	var ln net.Listener
	if funnel {
		ln, err = srv.ListenFunnel("tcp", ":443")
	} else {
		ln, err = srv.ListenTLS("tcp", ":443")
	}
	if err != nil {
		return fmt.Errorf("listen :443 (funnel=%v): %w", funnel, err)
	}
	defer ln.Close()

	emit(status{State: "running", FQDN: fqdn})

	httpSrv := &http.Server{Handler: proxy}
	go func() {
		<-ctx.Done()
		shutCtx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		_ = httpSrv.Shutdown(shutCtx)
	}()

	if err := httpSrv.Serve(ln); err != nil && err != http.ErrServerClosed {
		return fmt.Errorf("serve: %w", err)
	}
	return nil
}

// newProxy reverse-proxies to the loopback target, stamping the forwarded
// headers pdn's loopback-trusted middleware reads. X-Forwarded-For is left to
// the default Director (it appends the peer).
func newProxy(target, fqdn string) *httputil.ReverseProxy {
	backend := &url.URL{Scheme: "http", Host: target}
	proxy := httputil.NewSingleHostReverseProxy(backend)
	base := proxy.Director
	proxy.Director = func(r *http.Request) {
		base(r)
		r.Header.Set("X-Forwarded-Proto", "https")
		r.Header.Set("X-Forwarded-Host", fqdn)
	}
	return proxy
}

// watchLogin tails the IPN bus and emits needs-login with the BrowseToURL the
// control plane hands back when interactive auth is required.
func watchLogin(ctx context.Context, srv *tsnet.Server) {
	lc, err := srv.LocalClient()
	if err != nil {
		return
	}
	watcher, err := lc.WatchIPNBus(ctx, ipn.NotifyInitialState)
	if err != nil {
		return
	}
	defer watcher.Close()

	var once sync.Once
	for {
		n, err := watcher.Next()
		if err != nil {
			return // ctx cancelled or bus closed.
		}
		if n.BrowseToURL != nil && *n.BrowseToURL != "" {
			url := *n.BrowseToURL
			once.Do(func() { emit(status{State: "needs-login", AuthURL: url}) })
		}
	}
}

// waitForFQDN polls the local status until Self.DNSName is populated, then
// returns it with the trailing dot trimmed.
func waitForFQDN(ctx context.Context, srv *tsnet.Server) (string, error) {
	lc, err := srv.LocalClient()
	if err != nil {
		return "", fmt.Errorf("local client: %w", err)
	}
	for {
		st, err := lc.Status(ctx)
		if err == nil && st.Self != nil && st.Self.DNSName != "" {
			return strings.TrimSuffix(st.Self.DNSName, "."), nil
		}
		select {
		case <-ctx.Done():
			return "", ctx.Err()
		case <-time.After(250 * time.Millisecond):
		}
	}
}

func readAuthKey(path string) string {
	if path == "" {
		return ""
	}
	b, err := os.ReadFile(path)
	if err != nil {
		return "" // absent/unreadable → interactive login path.
	}
	return strings.TrimSpace(string(b))
}

// status is one line of the stdout JSON status stream the supervisor parses.
type status struct {
	State   string `json:"state"`
	AuthURL string `json:"authURL,omitempty"`
	FQDN    string `json:"fqdn,omitempty"`
	Error   string `json:"error,omitempty"`
}

var emitMu sync.Mutex

func emit(s status) {
	emitMu.Lock()
	defer emitMu.Unlock()
	b, _ := json.Marshal(s)
	fmt.Fprintln(os.Stdout, string(b))
}

// forward is one entry of the --forwards-file: a TCP listener on the tsnet
// node piped to a loopback target. tls "terminate" wraps the listener in the
// node's auto LE cert (the IMAPS/SMTPS case — the phone gets a trusted cert,
// the app stays plaintext on loopback); tls "raw" listens plaintext and relies
// on the tailnet's WireGuard encryption for the hop.
type forward struct {
	Listen int    `json:"listen"`
	Target string `json:"target"`
	TLS    string `json:"tls"`
}

// tsListener is the slice of *tsnet.Server that startForwards depends on,
// extracted as an interface so the wiring stays testable without a tailnet.
type tsListener interface {
	Listen(network, addr string) (net.Listener, error)
	ListenTLS(network, addr string) (net.Listener, error)
}

// parseForwards parses the --forwards-file bytes into validated forward
// entries. It is lenient by design: a malformed top-level document is an
// error (caller logs + runs with no forwards), but well-formed-but-bad
// individual entries are skipped (logged) rather than failing the whole set.
func parseForwards(jsonBytes []byte) ([]forward, error) {
	trimmed := strings.TrimSpace(string(jsonBytes))
	if trimmed == "" {
		return nil, nil // empty file ⇒ no forwards, not an error.
	}
	var raw []forward
	if err := json.Unmarshal([]byte(trimmed), &raw); err != nil {
		return nil, fmt.Errorf("forwards-file: %w", err)
	}
	out := make([]forward, 0, len(raw))
	for i, f := range raw {
		if f.Listen <= 0 || f.Listen > 65535 {
			fmt.Fprintf(os.Stderr, "forward[%d]: invalid listen port %d — skipping\n", i, f.Listen)
			continue
		}
		if f.Target == "" {
			fmt.Fprintf(os.Stderr, "forward[%d] (listen %d): empty target — skipping\n", i, f.Listen)
			continue
		}
		mode := f.TLS
		if mode == "" {
			mode = "raw" // default: plaintext over the tailnet's WireGuard.
		}
		if mode != "terminate" && mode != "raw" {
			fmt.Fprintf(os.Stderr, "forward[%d] (listen %d): unknown tls mode %q — skipping\n", i, f.Listen, f.TLS)
			continue
		}
		f.TLS = mode
		out = append(out, f)
	}
	return out, nil
}

// startForwards reads the forwards file, opens a tsnet listener per entry, and
// pumps each in a goroutine. It never returns an error: forwards are a
// best-effort overlay on the web path, so every failure is logged and the
// web reverse-proxy continues regardless.
func startForwards(ctx context.Context, srv tsListener, forwardsFile string) {
	if forwardsFile == "" {
		return
	}
	b, err := os.ReadFile(forwardsFile)
	if err != nil {
		fmt.Fprintf(os.Stderr, "forwards-file %q: %v — running with no forwards\n", forwardsFile, err)
		return
	}
	forwards, err := parseForwards(b)
	if err != nil {
		fmt.Fprintf(os.Stderr, "%v — running with no forwards\n", err)
		return
	}
	for _, f := range forwards {
		addr := fmt.Sprintf(":%d", f.Listen)
		var ln net.Listener
		if f.TLS == "terminate" {
			ln, err = srv.ListenTLS("tcp", addr)
		} else {
			ln, err = srv.Listen("tcp", addr)
		}
		if err != nil {
			fmt.Fprintf(os.Stderr, "forward %d (%s): listen failed: %v — skipping\n", f.Listen, f.TLS, err)
			continue
		}
		fmt.Fprintf(os.Stderr, "forward %d (%s) -> %s\n", f.Listen, f.TLS, f.Target)
		// Close the listener on ctx-cancel/SIGTERM so serveForward unblocks.
		go func(ln net.Listener) {
			<-ctx.Done()
			_ = ln.Close()
		}(ln)
		go serveForward(ln, f.Target)
	}
}

// serveForward accepts connections on ln and pipes each to a freshly-dialled
// plaintext TCP connection to target. The tls:terminate vs raw distinction is
// entirely in which listener the caller hands us — the byte-pump below is
// identical for both (and so is fully unit-testable with a plain net.Listener).
//
// A dial failure for one connection is logged and that conn closed; the
// listener keeps serving. serveForward returns when the listener is closed
// (ctx-cancel/SIGTERM), so it never crashes the sidecar.
func serveForward(ln net.Listener, target string) {
	for {
		conn, err := ln.Accept()
		if err != nil {
			// A closed listener (shutdown) is the normal exit, not an error.
			if errors.Is(err, net.ErrClosed) {
				return
			}
			// Transient accept errors: log and keep serving.
			fmt.Fprintf(os.Stderr, "forward -> %s: accept: %v\n", target, err)
			return
		}
		go pipeConn(conn, target)
	}
}

// pipeConn dials target and bidirectionally copies bytes between the accepted
// connection and the target until either side closes.
func pipeConn(client net.Conn, target string) {
	defer client.Close()
	upstream, err := net.Dial("tcp", target)
	if err != nil {
		fmt.Fprintf(os.Stderr, "forward -> %s: dial: %v\n", target, err)
		return
	}
	defer upstream.Close()

	var wg sync.WaitGroup
	wg.Add(2)
	cp := func(dst, src net.Conn) {
		defer wg.Done()
		_, _ = io.Copy(dst, src)
		// Half-close so the peer's Copy sees EOF and the pair tears down.
		if cw, ok := dst.(interface{ CloseWrite() error }); ok {
			_ = cw.CloseWrite()
		} else {
			_ = dst.Close()
		}
	}
	go cp(upstream, client)
	go cp(client, upstream)
	wg.Wait()
}
