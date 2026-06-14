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
	"flag"
	"fmt"
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
	flag.Parse()

	if *stateDir == "" {
		emit(status{State: "error", Error: "--state-dir is required"})
		os.Exit(1)
	}

	// SIGTERM/SIGINT → cancel ctx → graceful shutdown, exit 0.
	ctx, stop := signal.NotifyContext(context.Background(), syscall.SIGTERM, syscall.SIGINT)
	defer stop()

	if err := run(ctx, *hostname, *stateDir, *target, *authKeyFile, *funnel); err != nil {
		// A cancelled context is the clean SIGTERM path, not a fatal error.
		if ctx.Err() != nil {
			os.Exit(0)
		}
		emit(status{State: "error", Error: err.Error()})
		os.Exit(1)
	}
}

func run(ctx context.Context, hostname, stateDir, target, authKeyFile string, funnel bool) error {
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
