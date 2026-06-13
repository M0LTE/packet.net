using System.Net;
using System.Text;
using Packet.Mcp;
using Packet.Node.Mcp;

namespace Packet.Node.Tests.Mcp;

public class RestNodeMcpBackendTests
{
    // A stub handler that answers a fixed map of "VERB path" → (status, json body),
    // and records the requests it saw.
    private sealed class StubHandler(Dictionary<string, (HttpStatusCode, string)> responses)
        : HttpMessageHandler
    {
        public List<string> Seen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string key = $"{request.Method.Method} {request.RequestUri!.PathAndQuery}";
            Seen.Add(key);
            var (status, body) = responses.TryGetValue(key, out var r) ? r : (HttpStatusCode.NotFound, "");
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static RestNodeMcpBackend Backend(StubHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8080") });

    [Fact]
    public async Task List_ports_maps_the_rest_json()
    {
        var handler = new StubHandler(new()
        {
            ["GET /api/v1/ports"] = (HttpStatusCode.OK,
                """[{"id":"vhf","enabled":true,"state":"up","sessionCount":2,"lastError":null,"framesIn":10,"framesOut":7}]"""),
        });

        var ports = await Backend(handler).ListPortsAsync();

        var p = ports.Should().ContainSingle().Subject;
        p.Id.Should().Be("vhf");
        p.State.Should().Be("up");
        p.SessionCount.Should().Be(2);
        p.FramesIn.Should().Be(10);
        p.FramesOut.Should().Be(7);
    }

    [Fact]
    public async Task Recent_frames_filters_client_side_by_kind()
    {
        var handler = new StubHandler(new()
        {
            ["GET /api/v1/monitor/recent?limit=250"] = (HttpStatusCode.OK,
                """
                [{"seq":1,"timestamp":"2026-06-13T00:00:00Z","portId":"vhf","direction":"in","source":"A","dest":"B","type":"UI","length":20},
                 {"seq":2,"timestamp":"2026-06-13T00:00:01Z","portId":"vhf","direction":"out","source":"B","dest":"A","type":"RR","length":15}]
                """),
        });

        var frames = await Backend(handler).RecentFramesAsync(new FrameFilter(Kind: "UI"));

        frames.Should().ContainSingle().Which.Kind.Should().Be("UI");
    }

    [Fact]
    public async Task Reset_port_maps_status_codes()
    {
        var handler = new StubHandler(new()
        {
            ["POST /api/v1/ports/vhf/lifecycle"] = (HttpStatusCode.OK, ""),
            ["POST /api/v1/ports/ghost/lifecycle"] = (HttpStatusCode.NotFound, ""),
        });
        var backend = Backend(handler);

        (await backend.ResetPortAsync("vhf", McpCaller.LocalStdio)).Accepted.Should().BeTrue();
        (await backend.ResetPortAsync("ghost", McpCaller.LocalStdio)).Accepted.Should().BeFalse();
    }

    [Fact]
    public async Task Disconnect_maps_204_and_404()
    {
        var handler = new StubHandler(new()
        {
            ["DELETE /api/v1/sessions/vhf%3AM0LTE"] = (HttpStatusCode.NoContent, ""),
        });
        var backend = Backend(handler);

        (await backend.DisconnectSessionAsync("vhf:M0LTE", McpCaller.LocalStdio)).Accepted.Should().BeTrue();
        (await backend.DisconnectSessionAsync("vhf:NOBODY", McpCaller.LocalStdio)).Accepted.Should().BeFalse();
    }

    [Fact]
    public async Task Send_ui_frame_and_set_kiss_param_are_sse_only_over_the_bridge()
    {
        var backend = Backend(new StubHandler(new()));

        var send = await backend.SendUiFrameAsync(new SendUiRequest("vhf", "APRS", "hi"), McpCaller.LocalStdio);
        var kiss = await backend.SetKissParamAsync(new SetKissParamRequest("vhf", "txdelay", 40), McpCaller.LocalStdio);

        send.Accepted.Should().BeFalse();
        kiss.Accepted.Should().BeFalse();
    }
}
