using Microsoft.Extensions.Logging.Abstractions;
using Packet.Node.Core.Applications;
using Packet.Node.Core.Configuration;
using Packet.Node.Core.Console;
using Packet.Node.Tests.Support;

namespace Packet.Node.Tests.Applications;

/// <summary>
/// The console (application #0) dispatch to registered apps: a verb the console doesn't own is
/// offered to the <see cref="IApplicationHost"/>; a match launches it with the right context;
/// built-in verbs win first (an app can't shadow one); a non-matching verb is still "unknown
/// command"; and with no platform wired nothing changes. A recording stub host stands in for
/// the real launcher so the test asserts the console's routing, not process spawning.
/// </summary>
[Trait("Category", "Node")]
public sealed class ConsoleAppDispatchTests
{
    private static NodeCommandService Build(IApplicationHost? host)
    {
        var config = new TestConfigProvider(new NodeConfig
        {
            Identity = new Identity { Callsign = "M9YYY", Alias = "PDN" },
        });
        var env = new NodeConsoleEnvironment(config, outboundConnector: null, netRom: null, sysop: null, applications: host);
        return new NodeCommandService(env, NullLogger<NodeCommandService>.Instance);
    }

    // Drive the console with scripted lines, ending the session so RunAsync returns.
    private static async Task RunWith(NodeCommandService svc, DriveableConnection conn, params string[] lines)
    {
        foreach (var line in lines) conn.Inject(line + "\r");
        await svc.RunAsync(conn).WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task A_registered_verb_launches_the_app_with_callsign_transport_and_args()
    {
        var wall = new ApplicationConfig { Id = "wall", Match = "WALL", Command = "/bin/cat" };
        var host = new StubHost(wall);
        var svc = Build(host);
        var conn = new DriveableConnection("M0LTE-7", NodeTransportKind.Ax25);

        await RunWith(svc, conn, "WALL last 5", "B");

        Assert.Equal(1, host.RunCalls);
        Assert.Same(wall, host.RanApp);
        Assert.Equal("M0LTE-7", host.RanContext!.Callsign);
        Assert.Equal(NodeTransportKind.Ax25, host.RanContext.Transport);
        Assert.Equal(["last", "5"], host.RanContext.Args);
        Assert.DoesNotContain("Unknown command", conn.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_built_in_verb_is_never_offered_to_the_host()
    {
        // The stub would happily "resolve" BYE, but the parser claims it first as the disconnect
        // verb — so the host is never consulted and the user is disconnected.
        var host = new StubHost(new ApplicationConfig { Id = "x", Match = "BYE", Command = "/bin/cat" });
        var svc = Build(host);
        var conn = new DriveableConnection("M0LTE-7", NodeTransportKind.Ax25);

        await RunWith(svc, conn, "BYE");

        Assert.Equal(0, host.RunCalls);
        Assert.Contains("73", conn.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_non_matching_verb_is_still_unknown_command()
    {
        var host = new StubHost(new ApplicationConfig { Id = "wall", Match = "WALL", Command = "/bin/cat" });
        var svc = Build(host);
        var conn = new DriveableConnection("M0LTE-7", NodeTransportKind.Ax25);

        await RunWith(svc, conn, "ZZZ", "B");

        Assert.Equal(0, host.RunCalls);
        Assert.Contains("Unknown command", conn.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_platform_wired_an_unknown_verb_is_unknown_command()
    {
        var svc = Build(host: null);
        var conn = new DriveableConnection("M0LTE-7", NodeTransportKind.Ax25);

        await RunWith(svc, conn, "WALL", "B");

        Assert.Contains("Unknown command", conn.Output, StringComparison.Ordinal);
    }

    // Records what the console asked of the launcher; resolves the one configured app by an
    // exact case-insensitive match, mirroring the real host's contract.
    private sealed class StubHost(ApplicationConfig app) : IApplicationHost
    {
        public int RunCalls { get; private set; }
        public ApplicationConfig? RanApp { get; private set; }
        public NodeAppContext? RanContext { get; private set; }

        public ApplicationConfig? Resolve(string verb) =>
            string.Equals(verb?.Trim(), app.Match, StringComparison.OrdinalIgnoreCase) ? app : null;

        public Task RunAsync(ApplicationConfig a, INodeConnection session, NodeAppContext context, CancellationToken ct = default)
        {
            RunCalls++;
            RanApp = a;
            RanContext = context;
            return Task.CompletedTask;
        }
    }
}
