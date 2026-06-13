using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Packet.Mcp;

namespace Packet.Node.Mcp;

/// <summary>
/// The <see cref="INodeMcpBackend"/> the <c>pdn mcp</c> stdio entrypoint uses: an
/// HTTP client of the <b>running</b> node's loopback REST API. A <c>pdn mcp</c>
/// subcommand is a separate process and can't share the live node's in-proc state,
/// so this bridges stdio to the node over <c>127.0.0.1</c>. The read tools +
/// <c>reset_port</c>/<c>disconnect_session</c> map onto existing <c>/api/v1</c>
/// endpoints; <c>send_ui_frame</c>/<c>set_kiss_param</c> have no REST endpoint and
/// report honestly that they're SSE-transport-only. <c>decode_frame</c> is pure and
/// never reaches here. See docs/mcp-design.md.
/// </summary>
public sealed class RestNodeMcpBackend(HttpClient http) : INodeMcpBackend
{
    private readonly HttpClient http = http;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---- read ----

    public async Task<IReadOnlyList<McpPortStatus>> ListPortsAsync(CancellationToken ct = default)
    {
        var ports = await GetAsync<List<RestPort>>("api/v1/ports", ct).ConfigureAwait(false) ?? [];
        return ports.Select(p => new McpPortStatus(
            p.Id, p.Enabled, p.State ?? "?", p.SessionCount, p.FramesIn, p.FramesOut)).ToList();
    }

    public async Task<IReadOnlyList<McpSessionInfo>> ListSessionsAsync(CancellationToken ct = default)
    {
        var s = await GetAsync<List<RestSession>>("api/v1/sessions", ct).ConfigureAwait(false) ?? [];
        return s.Select(x => new McpSessionInfo(
            x.Id, x.PortId, x.Peer, x.Role ?? "?", x.State ?? "?", x.Vs, x.Vr, x.Window,
            x.UptimeSeconds, x.BytesIn, x.BytesOut, x.LastActivity ?? "—")).ToList();
    }

    public async Task<IReadOnlyList<McpMonitorFrame>> RecentFramesAsync(FrameFilter filter, CancellationToken ct = default)
    {
        int limit = Math.Clamp(filter.Limit ?? 250, 1, 250);
        var frames = await GetAsync<List<RestMonitor>>("api/v1/monitor/recent?limit=250", ct).ConfigureAwait(false)
            ?? [];

        IEnumerable<RestMonitor> q = frames;
        if (!string.IsNullOrWhiteSpace(filter.Port))
        {
            q = q.Where(f => string.Equals(f.PortId, filter.Port, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(filter.Peer))
        {
            q = q.Where(f => string.Equals(f.Source, filter.Peer, StringComparison.OrdinalIgnoreCase)
                || string.Equals(f.Dest, filter.Peer, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(filter.Kind))
        {
            q = q.Where(f => string.Equals(f.Type, filter.Kind, StringComparison.OrdinalIgnoreCase));
        }
        if (filter.SinceSeconds is { } secs && secs > 0)
        {
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(secs);
            q = q.Where(f => f.Timestamp >= cutoff);
        }

        return q.TakeLast(limit)
            .Select(f => new McpMonitorFrame(
                f.Seq, f.Timestamp, f.PortId, f.Direction ?? "?", f.Source, f.Dest, f.Type ?? "?", f.Length))
            .ToList();
    }

    public async Task<McpLinkQuality> LinkQualityAsync(string remote, string? portId = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);
        var links = await GetAsync<List<RestLink>>("api/v1/links", ct).ConfigureAwait(false) ?? [];
        var link = links.FirstOrDefault(l =>
            string.Equals(l.Peer, remote, StringComparison.OrdinalIgnoreCase)
            && (portId is null || string.Equals(l.PortId, portId, StringComparison.OrdinalIgnoreCase)));

        return link is null
            ? new McpLinkQuality(portId ?? "?", remote, 0, 0, 0, 0, 0, 0, Unknown: true)
            : new McpLinkQuality(link.PortId, link.Peer, link.SmoothedRttMs, link.Retries,
                link.RejCount, link.SrejCount, link.FramesIn, link.FramesOut, Unknown: false);
    }

    public async Task<McpNetworkTopology> NetworkTopologyAsync(CancellationToken ct = default)
    {
        var topo = await GetAsync<RestTopology>("api/v1/netrom/routes", ct).ConfigureAwait(false);
        if (topo is null)
        {
            return new McpNetworkTopology(DateTimeOffset.MinValue, [], []);
        }

        var neighbours = (topo.Neighbours ?? [])
            .Select(n => new McpNeighbour(n.Neighbour, n.Alias, n.PortId ?? "?", n.PathQuality, n.LastHeard ?? "—"))
            .ToList();
        var destinations = (topo.Destinations ?? [])
            .Select(d => new McpDestination(d.Destination, d.Alias,
                (d.Routes ?? []).Select(r => new McpRoute(r.Neighbour, r.Quality, r.Obsolescence)).ToList()))
            .ToList();
        return new McpNetworkTopology(topo.GeneratedAt, neighbours, destinations);
    }

    // ---- write ----

    public Task<SendResult> SendUiFrameAsync(SendUiRequest req, McpCaller caller, CancellationToken ct = default)
        => Task.FromResult(new SendResult(false,
            "send_ui_frame is only available over the in-process SSE transport, not the stdio bridge."));

    public async Task<PortActionResult> ResetPortAsync(string portId, McpCaller caller, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portId);
        using var resp = await http.PostAsJsonAsync(
            $"api/v1/ports/{Uri.EscapeDataString(portId)}/lifecycle", new { action = "restart" }, Json, ct)
            .ConfigureAwait(false);

        return resp.StatusCode switch
        {
            HttpStatusCode.OK => new PortActionResult(true, portId, $"port '{portId}' restarted."),
            HttpStatusCode.NotFound => new PortActionResult(false, portId, $"no such port '{portId}'."),
            HttpStatusCode.Conflict => new PortActionResult(false, portId, $"port '{portId}' is disabled (enable it first)."),
            _ => new PortActionResult(false, portId, $"restart failed ({(int)resp.StatusCode})."),
        };
    }

    public async Task<SessionResult> DisconnectSessionAsync(string sessionId, McpCaller caller, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        using var resp = await http.DeleteAsync(
            $"api/v1/sessions/{Uri.EscapeDataString(sessionId)}", ct).ConfigureAwait(false);

        return resp.StatusCode switch
        {
            HttpStatusCode.NoContent => new SessionResult(true, sessionId, "disconnect requested."),
            HttpStatusCode.NotFound => new SessionResult(false, sessionId, "no such session."),
            _ => new SessionResult(false, sessionId, $"disconnect failed ({(int)resp.StatusCode})."),
        };
    }

    public Task<KissParamResult> SetKissParamAsync(SetKissParamRequest req, McpCaller caller, CancellationToken ct = default)
        => Task.FromResult(new KissParamResult(false, false,
            "set_kiss_param is not available over the stdio bridge (and not yet wired to the KISS modem)."));

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
        => await http.GetFromJsonAsync<T>(path, Json, ct).ConfigureAwait(false);

    // ---- REST wire shapes (camelCase via the node's STJ web defaults) ----

    private sealed record RestPort(string Id, bool Enabled, string? State, int SessionCount, long FramesIn, long FramesOut);

    private sealed record RestSession(
        string Id, string PortId, string Peer, string? Role, string? State,
        int Vs, int Vr, int Window, long UptimeSeconds, long BytesIn, long BytesOut, string? LastActivity);

    private sealed record RestMonitor(
        long Seq, DateTimeOffset Timestamp, string PortId, string? Direction,
        string Source, string Dest, string? Type, int Length);

    private sealed record RestLink(
        string PortId, string Peer, int SmoothedRttMs, int Retries,
        int RejCount, int SrejCount, long FramesIn, long FramesOut);

    private sealed record RestTopology(
        DateTimeOffset GeneratedAt, List<RestNeighbour>? Neighbours, List<RestDestination>? Destinations);

    private sealed record RestNeighbour(string Neighbour, string? Alias, string? PortId, int PathQuality, string? LastHeard);

    private sealed record RestDestination(string Destination, string? Alias, List<RestRoute>? Routes);

    private sealed record RestRoute(string Neighbour, int Quality, int Obsolescence);
}
