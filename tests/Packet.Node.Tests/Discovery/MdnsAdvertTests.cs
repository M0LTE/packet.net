using Packet.Node.Core.Configuration;
using Packet.Node.Core.Discovery;
using Xunit;

namespace Packet.Node.Tests.Discovery;

public sealed class MdnsAdvertTests
{
    private static NodeConfig Cfg(
        bool enabled,
        string bind = "0.0.0.0",
        int port = 8080,
        string callsign = "M0LTE-7",
        string? alias = null,
        string? instance = null) =>
        new()
        {
            Identity = new Identity { Callsign = callsign, Alias = alias },
            Management = new ManagementConfig
            {
                Http = new HttpConfig { Bind = bind, Port = port },
                Mdns = new MdnsConfig { Enabled = enabled, InstanceName = instance },
            },
        };

    [Fact]
    public void Disabled_yields_no_plan()
    {
        var plan = MdnsAdvert.Plan(Cfg(enabled: false), "1.0", out var reason);
        Assert.Null(plan);
        Assert.Contains("enabled", reason);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.53")]
    [InlineData("::1")]
    [InlineData("localhost")]
    public void Loopback_bind_yields_no_plan(string bind)
    {
        var plan = MdnsAdvert.Plan(Cfg(enabled: true, bind: bind), "1.0", out var reason);
        Assert.Null(plan);
        Assert.Contains("loopback", reason);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("192.168.1.10")]
    public void Non_loopback_bind_advertises(string bind)
    {
        var plan = MdnsAdvert.Plan(Cfg(enabled: true, bind: bind), "1.0", out var reason);
        Assert.NotNull(plan);
        Assert.Null(reason);
    }

    [Fact]
    public void Plan_carries_callsign_as_instance_and_cs_txt()
    {
        var plan = MdnsAdvert.Plan(Cfg(enabled: true, callsign: "M0LTE-7", port: 8080), "0.18.1", out _);
        Assert.NotNull(plan);
        Assert.Equal("M0LTE-7", plan!.Instance);
        Assert.Equal(8080, plan.Port);
        Assert.Contains("cs=M0LTE-7", plan.Txt);
    }

    [Fact]
    public void Alias_rides_a_name_txt_but_callsign_stays_the_instance()
    {
        var plan = MdnsAdvert.Plan(Cfg(enabled: true, callsign: "M0LTE-7", alias: "RDGBBS"), "0.18.1", out _);
        Assert.NotNull(plan);
        Assert.Equal("M0LTE-7", plan!.Instance); // identity stays the callsign, not the (collidable) alias
        Assert.Contains("name=RDGBBS", plan.Txt);
    }

    [Fact]
    public void No_alias_means_no_name_txt()
    {
        var plan = MdnsAdvert.Plan(Cfg(enabled: true, alias: null), "0.18.1", out _);
        Assert.DoesNotContain(plan!.Txt, t => t.StartsWith("name=", System.StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0.18.1", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Version_txt_present_only_when_version_is_set(string? version, bool expected)
    {
        var plan = MdnsAdvert.Plan(Cfg(enabled: true), version, out _);
        Assert.Equal(expected, plan!.Txt.Any(t => t.StartsWith("v=", System.StringComparison.Ordinal)));
    }

    [Fact]
    public void Explicit_instance_name_overrides_the_callsign_default()
    {
        var plan = MdnsAdvert.Plan(Cfg(enabled: true, callsign: "M0LTE-7", instance: "Hilltop"), "1.0", out _);
        Assert.Equal("Hilltop", plan!.Instance);
        Assert.Contains("cs=M0LTE-7", plan.Txt); // cs is still the callsign, not the display name
    }

    [Fact]
    public void ToAvahiArgs_is_f_s_endopts_instance_type_port_then_txt()
    {
        var plan = MdnsAdvert.Plan(Cfg(enabled: true, callsign: "M0LTE-7", alias: "RDGBBS", port: 8080), "0.18.1", out _);
        var args = plan!.ToAvahiArgs();
        Assert.Equal("-f", args[0]);   // --no-fail: wait for / reattach to avahi-daemon
        Assert.Equal("-s", args[1]);
        Assert.Equal("--", args[2]);   // end options: an instance name can't be read as a flag
        Assert.Equal("M0LTE-7", args[3]);
        Assert.Equal("_pdn._tcp", args[4]);
        Assert.Equal("8080", args[5]);
        Assert.Contains("cs=M0LTE-7", args);
        Assert.Contains("name=RDGBBS", args);
        Assert.Contains("v=0.18.1", args);
    }

    [Fact]
    public void Out_of_range_port_yields_no_plan()
    {
        var plan = MdnsAdvert.Plan(Cfg(enabled: true, port: 0), "1.0", out var reason);
        Assert.Null(plan);
        Assert.Contains("port", reason);
    }
}
