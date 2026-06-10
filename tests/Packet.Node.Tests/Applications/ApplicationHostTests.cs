using Packet.Node.Core.Applications;
using Packet.Node.Core.Configuration;
using Packet.Node.Core.Console;
using Packet.Node.Tests.Support;

namespace Packet.Node.Tests.Applications;

/// <summary>
/// The <see cref="ApplicationHost"/>: verb resolution (enabled-only, case-insensitive, exact,
/// read live from config so a hot edit applies to the next launch) and the total run contract
/// (a spawn failure is reported to the user, never thrown).
/// </summary>
[Trait("Category", "Node")]
public sealed class ApplicationHostTests
{
    private static NodeConfig WithApps(params ApplicationConfig[] apps) => new()
    {
        Identity = new Identity { Callsign = "M0LTE-1" },
        Applications = apps,
    };

    private static ApplicationConfig App(string id, string match, bool enabled = true, string command = "/bin/cat") =>
        new() { Id = id, Match = match, Enabled = enabled, Command = command };

    [Fact]
    public void Resolve_matches_an_enabled_app_case_insensitively_and_exactly()
    {
        var cfg = new TestConfigProvider(WithApps(App("wall", "WALL")));
        var host = new ApplicationHost(cfg);

        Assert.Equal("wall", host.Resolve("WALL")?.Id);
        Assert.Equal("wall", host.Resolve("wall")?.Id);   // case-insensitive
        Assert.Equal("wall", host.Resolve(" WALL ")?.Id);  // trimmed
        Assert.Null(host.Resolve("WAL"));                  // exact, not a prefix
        Assert.Null(host.Resolve("WALLY"));
        Assert.Null(host.Resolve("nope"));
        Assert.Null(host.Resolve(""));
    }

    [Fact]
    public void Resolve_ignores_a_disabled_app()
    {
        var host = new ApplicationHost(new TestConfigProvider(WithApps(App("wall", "WALL", enabled: false))));
        Assert.Null(host.Resolve("WALL"));
    }

    [Fact]
    public void Resolve_reads_config_live()
    {
        var cfg = new TestConfigProvider(WithApps(App("wall", "WALL")));
        var host = new ApplicationHost(cfg);
        Assert.NotNull(host.Resolve("WALL"));

        // Hot edit: disable wall, add guest. The next resolve reflects it — no reconcile needed.
        cfg.Apply(WithApps(App("wall", "WALL", enabled: false), App("guest", "GUEST")));
        Assert.Null(host.Resolve("WALL"));
        Assert.Equal("guest", host.Resolve("GUEST")?.Id);
    }

    [Fact]
    public async Task RunAsync_reports_a_spawn_failure_to_the_user_and_does_not_throw()
    {
        var bad = App("ghost", "GHOST", command: "/no/such/binary-xyzzy");
        var host = new ApplicationHost(new TestConfigProvider(WithApps(bad)));
        var conn = new DriveableConnection("M0LTE-7", NodeTransportKind.Ax25);

        var ctx = new NodeAppContext { Callsign = "M0LTE-7", Transport = NodeTransportKind.Ax25 };
        await host.RunAsync(bad, conn, ctx);   // must not throw

        Assert.Contains("unavailable", conn.Output, StringComparison.OrdinalIgnoreCase);
    }
}
