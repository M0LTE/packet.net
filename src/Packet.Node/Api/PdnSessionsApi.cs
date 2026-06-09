using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Packet.Ax25;
using Packet.Ax25.Session;
using Packet.Core;
using Packet.Node.Core.Api;
using Packet.Node.Core.Console;
using Packet.Node.Core.Hosting;

namespace Packet.Node.Api;

/// <summary>
/// The session-action + ping side of the pdn node control API (Slice 3, step 4): the
/// direct-supervisor actions the web Sessions screen and the ping tool drive —
/// connect-out (<c>POST /sessions</c>), disconnect (<c>DELETE /sessions/{id}</c>), send
/// one line into a connected-mode session (<c>POST /sessions/{id}/send</c>), and the
/// connectionless TEST ping (<c>POST /ping</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every action that touches the live port/session set runs under the host's exclusive
/// gate (<see cref="NodeHostedService.RunExclusiveAsync{T}"/>) — the same gate the
/// reconcile worker holds — so a web action can never race a config reconcile (or another
/// action) mutating ports or sessions. The critical sections are kept short: the gate is
/// held only to <em>capture</em> a listener/session reference or to post a single event;
/// the one long-running operation, a connect-out dial that awaits SABM/UA, runs
/// <em>outside</em> the gate so it doesn't block reconciles for the dial's duration.
/// </para>
/// <para>
/// <b>Connect-out (v1 scope).</b> A web connect-out opens the session via the supervisor's
/// resolved connector — which already encodes "a callsign dials out over AX.25 on the
/// local channel, a NET/ROM alias routes across the network" (the same logic the console's
/// <c>Connect</c> command uses) — and surfaces the new session in <c>/sessions</c>. There
/// is <b>no</b> console-bridge / received-data streaming in v1: this endpoint does not run
/// a node-command service over the opened connection, and there is no per-session I/O
/// stream (the live monitor shows the frames). <c>portId</c> in the request body is
/// validated (it must name a running port when supplied) but the dial itself goes through
/// the supervisor's <em>default</em>-resolved connector — a per-<c>portId</c> dial selector
/// needs a per-port connector factory on the supervisor that is a named later step; v1
/// dials on the deterministic default port / best NET/ROM route.
/// </para>
/// <para>
/// <b>Ping is deferred</b> to a clear 501 (see <c>MapPdnSessionsApi</c>): a connectionless
/// TEST ping needs a public "send a TEST command frame + correlate the TEST response"
/// path on <see cref="Ax25Listener"/> that does not exist yet (the listener exposes
/// connected-mode <see cref="Ax25Listener.SendData"/> and UI-frame
/// <see cref="Ax25Listener.SendUiAsync"/>, but no TEST send, and its modem is private).
/// Building it would mean widening the AX.25 library, which is out of scope for this
/// endpoint pass — so the endpoint returns 501 with an explicit "later step" message
/// rather than half-building it.
/// </para>
/// <para>
/// Auth is a later step — like the read API, the SSE feed, the config write API, and the
/// port-management API, these are unauthenticated and the node binds 127.0.0.1 by default.
/// RTT for the (deferred) ping would come from the injected <see cref="TimeProvider"/> /
/// <c>Stopwatch</c>, never <c>DateTime.Now</c> (repo rule §2.7).
/// </para>
/// </remarks>
public static class PdnSessionsApi
{
    // Bound a connect-out dial: the request token, linked with a ceiling so a wedged
    // SABM/UA exchange can't hold a server thread indefinitely. The listener's own
    // (N2+1)·T1V backstop is usually tighter, but this is a hard outer bound.
    private static readonly TimeSpan DialTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Map the session-action + ping endpoints under <c>/api/v1</c>. Called from the node
    /// composition root after the port-management API and before the SPA fallback (the
    /// specific routes win over the <c>/api/{**rest}</c> catch-all regardless of order).
    /// </summary>
    public static void MapPdnSessionsApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var v1 = app.MapGroup("/api/v1");

        // Connect out to a callsign (AX.25 dial) or NET/ROM alias (network route). Capture
        // the connector inside the gate; dial OUTSIDE it (bounded by DialTimeout + the
        // request token). Returns the new session's SessionInfo on success.
        v1.MapPost("/sessions", async (ConnectRequest body, NodeHostedService host, TimeProvider clock, CancellationToken ct) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.Target))
            {
                return Results.BadRequest(new { error = "A 'target' callsign or NET/ROM alias is required." });
            }
            if (!Callsign.TryParse(body.Target.Trim(), out var target))
            {
                return Results.BadRequest(new { error = $"'{body.Target}' is not a valid callsign or NET/ROM alias." });
            }

            // Capture a connector under the gate (a short critical section — no dial here).
            // The supervisor's resolver encodes callsign→AX.25-dial / alias→NET/ROM-route
            // AND claims the dialled remote so its SessionAccepted handler doesn't start a
            // node console against the station we're dialling.
            var (connector, portUnknown) = await host.RunExclusiveAsync(() =>
            {
                if (host.Supervisor is null)
                {
                    return Task.FromResult<(IOutboundConnector?, bool)>((null, false));
                }
                // If a portId was named, it must be a running port (honoured as validation;
                // see the type remarks for the v1 default-connector dial scope).
                if (!string.IsNullOrWhiteSpace(body.PortId) && host.Supervisor.GetPort(body.PortId!) is null)
                {
                    return Task.FromResult<(IOutboundConnector?, bool)>((null, true));
                }
                return Task.FromResult((host.Supervisor.ResolveDefaultConnector(), false));
            }, ct).ConfigureAwait(false);

            if (portUnknown)
            {
                return Results.NotFound(new { error = $"Port '{body.PortId}' is not running." });
            }
            if (connector is null)
            {
                return Results.NotFound(new { error = "No running port to connect out on." });
            }

            // Dial OUTSIDE the gate — awaiting SABM/UA (or a NET/ROM circuit) is the
            // long-running part and must not block config reconciles. The ceiling timer
            // rides the injected TimeProvider (repo rule §2.7: no wall-clock), linked with
            // the request token.
            using var ceiling = new CancellationTokenSource(DialTimeout, clock);
            using var dialCts = CancellationTokenSource.CreateLinkedTokenSource(ct, ceiling.Token);
            INodeConnection connection;
            try
            {
                connection = await connector.ConnectAsync(target, dialCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Results.Json(
                    new { error = $"Connect to {target} timed out after {DialTimeout.TotalSeconds:F0}s." },
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (TimeoutException ex)
            {
                return Results.Json(
                    new { error = $"Connect to {target} timed out: {ex.Message}" },
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // No route / refused (DM) / no local port to dial it on.
                return Results.Json(
                    new { error = $"Connect to {target} failed: {ex.Message}" },
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var info = await host.RunExclusiveAsync(
                () => Task.FromResult(ProjectConnected(host, connector.PortId, connection, target, clock)), ct)
                .ConfigureAwait(false);
            return Results.Ok(info);
        });

        // Disconnect a session: find it by {id} (portId:peer), post DL-DISCONNECT under the
        // gate. Absent → 404, else 204.
        v1.MapDelete("/sessions/{id}", async (string id, NodeHostedService host, CancellationToken ct) =>
        {
            var found = await host.RunExclusiveAsync(() =>
            {
                var match = FindSession(host, id);
                match?.Session.PostEvent(new DlDisconnectRequest());
                return Task.FromResult(match is not null);
            }, ct).ConfigureAwait(false);

            return found ? Results.NoContent() : Results.NotFound();
        });

        // Send one text line into a connected-mode session. The line is UTF-8 with a
        // trailing CR (the node's console line discipline — CR, not CRLF). Absent → 404,
        // else 202 (queued: SendData hands the request to the session's send path).
        v1.MapPost("/sessions/{id}/send", async (string id, SendRequest body, NodeHostedService host, CancellationToken ct) =>
        {
            if (body is null || body.Line is null)
            {
                return Results.BadRequest(new { error = "A 'line' is required." });
            }

            var sent = await host.RunExclusiveAsync(() =>
            {
                if (FindSession(host, id) is not { } match)
                {
                    return Task.FromResult(false);
                }
                // CR-terminated, UTF-8 — matches the telnet console's CR (not CRLF) relay
                // discipline onto the AX.25 link.
                var bytes = Encoding.UTF8.GetBytes(body.Line + "\r");
                match.Listener.SendData(match.Session, bytes, Ax25Frame.PidNoLayer3);
                return Task.FromResult(true);
            }, ct).ConfigureAwait(false);

            return sent ? Results.Accepted() : Results.NotFound();
        });

        // Connectionless TEST ping — DEFERRED (501). See the type remarks: it needs a
        // public TEST-frame send+correlate path on Ax25Listener that doesn't exist yet, and
        // building it would widen the AX.25 library (out of scope for this endpoint pass).
        v1.MapPost("/ping", () => Results.Json(
            new
            {
                error = "Connectionless TEST ping is not implemented yet — it is a later step "
                      + "(needs a TEST command-frame send + response-correlation path on the "
                      + "AX.25 listener; only connected-mode send + UI-frame send exist today).",
            },
            statusCode: StatusCodes.Status501NotImplemented));
    }

    /// <summary>The connect-out request body: a callsign or NET/ROM alias, optionally a port.</summary>
    public sealed record ConnectRequest(string Target, string? PortId = null);

    /// <summary>The send-line request body: one line of text (CR-terminated on the wire).</summary>
    public sealed record SendRequest(string Line);

    /// <summary>A live session matched from a <c>{portId}:{peer}</c> id, with its owning listener.</summary>
    private readonly record struct SessionMatch(string PortId, Ax25Listener Listener, Ax25Session Session);

    /// <summary>
    /// Split a session id at the FIRST ':' into (portId, peer) — the convention
    /// <c>PdnReadApi.BuildSessions</c> mints (<c>$"{portId}:{peer}"</c>). The peer (a
    /// callsign with an SSID, e.g. <c>M0LTE-1</c>) itself contains no ':', so a single
    /// split on the first ':' is unambiguous. Returns false if there is no ':' .
    /// </summary>
    internal static bool TrySplitSessionId(string id, out string portId, out string peer)
    {
        portId = string.Empty;
        peer = string.Empty;
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }
        int colon = id.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon >= id.Length - 1)
        {
            return false;
        }
        portId = id[..colon];
        peer = id[(colon + 1)..];
        return true;
    }

    // Resolve a {portId:peer} id to the live session on that port (matched on the peer's
    // canonical text), or null if the port isn't running / the peer has no live session /
    // the id is malformed. Caller holds the gate.
    private static SessionMatch? FindSession(NodeHostedService host, string id)
    {
        if (!TrySplitSessionId(id, out var portId, out var peer))
        {
            return null;
        }
        var listener = host.Supervisor?.GetPort(portId)?.Listener;
        var session = listener?.ActiveSessions.FirstOrDefault(s => s.Context.Remote.ToString() == peer);
        return listener is not null && session is not null
            ? new SessionMatch(portId, listener, session)
            : null;
    }

    // Project the SessionInfo for a freshly-opened connect-out. Prefer the AX.25 session
    // the connection wraps (Ax25NodeConnection.Session) so V(S)/V(R)/state are exact; for a
    // NET/ROM circuit (no AX.25 session) fall back to the live ActiveSessions on the port,
    // then to a minimal Connected projection from the connection's peer id. Caller holds
    // the gate.
    private static SessionInfo ProjectConnected(
        NodeHostedService host, string portId, INodeConnection connection, Callsign target, TimeProvider clock)
    {
        var neighbours = PdnReadApi.NeighbourCallsigns(host);
        var now = clock.GetUtcNow();

        if (connection is Ax25NodeConnection ax25)
        {
            return PdnReadApi.ProjectSession(host, portId, ax25.Session, neighbours, now);
        }

        // NET/ROM (or other) connection: try to find a matching live AX.25 session on the
        // port; else project a minimal Connected row from the connection's peer.
        var peer = connection.PeerId;
        var session = host.Supervisor?.GetPort(portId)?.Listener.ActiveSessions
            .FirstOrDefault(s => s.Context.Remote.ToString() == peer);
        if (session is not null)
        {
            return PdnReadApi.ProjectSession(host, portId, session, neighbours, now);
        }

        var who = string.IsNullOrEmpty(peer) ? target.ToString() : peer;
        return new SessionInfo(
            Id: $"{portId}:{who}",
            PortId: portId,
            Peer: who,
            Role: neighbours.Contains(who) ? "interlink" : "console",
            State: "Connected",
            Vs: 0,
            Vr: 0,
            Window: 0,
            UptimeSeconds: 0,
            BytesIn: 0,
            BytesOut: 0,
            LastActivity: "0:00:00");
    }
}
