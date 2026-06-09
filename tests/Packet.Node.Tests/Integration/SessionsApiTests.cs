using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Packet.Node.Api;

namespace Packet.Node.Tests.Integration;

/// <summary>
/// Boots the real <c>Packet.Node</c> composition root and exercises the Slice 3
/// session-action + ping API (step 4): <c>POST /api/v1/sessions</c> (connect-out),
/// <c>DELETE /api/v1/sessions/{id}</c> (disconnect), <c>POST /api/v1/sessions/{id}/send</c>
/// (send a line), and <c>POST /api/v1/ping</c>. Mirrors <see cref="PortsApiTests"/> — a
/// temp YAML config with no ports and telnet disabled (so no fixed TCP port is bound under
/// the WAF), the routing store in the same temp dir.
/// </summary>
/// <remarks>
/// The actual connect / send / disconnect against a real peer can't be WAF-tested (no
/// modem): the human live-verifies against GB7RDG. These cover the deterministically
/// reachable contract — the 4xx/5xx behaviours when there is no live session / no running
/// port / a bad target — plus the deferred ping's 501 shape and the pure session-id split
/// helper (<see cref="SessionIdSplitTests"/>).
/// </remarks>
[Trait("Category", "Node")]
public sealed class SessionsApiTests : IDisposable
{
    private const string Callsign = "M0LTE-1";
    private readonly string configPath;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public SessionsApiTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "packetnet-sessionsapi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        configPath = Path.Combine(dir, "node.yaml");
        // No ports + telnet off: the WAF host binds no fixed TCP port and has no running
        // listener — so every connect/disconnect/send hits its no-live-session path.
        File.WriteAllText(configPath, $"""
            schemaVersion: 1
            identity:
              callsign: {Callsign}
              alias: LONDON
            ports: []
            management:
              telnet:
                enabled: false
              http:
                bind: 127.0.0.1
                port: 8080
            """);
        Environment.SetEnvironmentVariable("PACKETNET_CONFIG", configPath);
        Environment.SetEnvironmentVariable("PACKETNET_DB", Path.Combine(dir, "pdn.db"));
    }

    private sealed class NodeAppFactory : WebApplicationFactory<Program>;

    [Fact]
    public async Task Connect_with_no_running_port_returns_404()
    {
        await using var factory = new NodeAppFactory();
        using var client = factory.CreateClient();

        // A parseable target, but no port is up to dial it on.
        var resp = await client.PostAsJsonAsync("/api/v1/sessions", new { target = "GB7RDG-1" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Connect_with_an_unparseable_target_returns_400()
    {
        await using var factory = new NodeAppFactory();
        using var client = factory.CreateClient();

        // A target that can't be a callsign or alias (lowercase + symbols).
        var resp = await client.PostAsJsonAsync("/api/v1/sessions", new { target = "not a call!" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Connect_with_a_missing_target_returns_400()
    {
        await using var factory = new NodeAppFactory();
        using var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/v1/sessions", new { target = "" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Connect_naming_an_unknown_port_returns_404()
    {
        await using var factory = new NodeAppFactory();
        using var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/v1/sessions",
            new { target = "GB7RDG-1", portId = "ghost" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Disconnect_an_unknown_session_returns_404()
    {
        await using var factory = new NodeAppFactory();
        using var client = factory.CreateClient();

        var resp = await client.DeleteAsync("/api/v1/sessions/vhf:GB7RDG-1");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Disconnect_a_malformed_session_id_returns_404()
    {
        await using var factory = new NodeAppFactory();
        using var client = factory.CreateClient();

        // No ':' → not a valid session id → no match → 404.
        var resp = await client.DeleteAsync("/api/v1/sessions/garbage");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Send_to_an_unknown_session_returns_404()
    {
        await using var factory = new NodeAppFactory();
        using var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/v1/sessions/vhf:GB7RDG-1/send",
            new { line = "hello" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ping_is_deferred_with_a_501_and_a_message()
    {
        await using var factory = new NodeAppFactory();
        using var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/v1/ping",
            new { station = "GB7RDG-1", portId = "vhf", count = 3 });
        resp.StatusCode.Should().Be(HttpStatusCode.NotImplemented);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("error").GetString().Should().Contain("not implemented");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Environment.SetEnvironmentVariable("PACKETNET_CONFIG", null);
        Environment.SetEnvironmentVariable("PACKETNET_DB", null);
        try
        {
            var dir = Path.GetDirectoryName(configPath);
            if (dir is not null) Directory.Delete(dir, recursive: true);
        }
        catch { /* best effort */ }
    }
}

/// <summary>
/// Unit tests for the pure session-id split helper (<c>PdnSessionsApi.TrySplitSessionId</c>)
/// — the contract that mirrors <c>PdnReadApi.BuildSessions</c>'s <c>$"{portId}:{peer}"</c>
/// id minting. The peer (a callsign, possibly SSID'd) contains no ':', so a split on the
/// FIRST ':' is unambiguous.
/// </summary>
[Trait("Category", "Node")]
public sealed class SessionIdSplitTests
{
    [Theory]
    [InlineData("vhf:GB7RDG-1", "vhf", "GB7RDG-1")]
    [InlineData("link-dn:M0LTE", "link-dn", "M0LTE")]
    [InlineData("a:b", "a", "b")]
    public void Splits_at_the_first_colon(string id, string expectedPort, string expectedPeer)
    {
        PdnSessionsApi.TrySplitSessionId(id, out var port, out var peer).Should().BeTrue();
        port.Should().Be(expectedPort);
        peer.Should().Be(expectedPeer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("noColon")]
    [InlineData(":leadingColon")]
    [InlineData("trailingColon:")]
    public void Rejects_ids_without_a_well_formed_split(string id)
    {
        PdnSessionsApi.TrySplitSessionId(id, out _, out _).Should().BeFalse();
    }
}
