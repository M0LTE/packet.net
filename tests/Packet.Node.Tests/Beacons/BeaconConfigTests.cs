using Packet.Node.Core.Configuration;
using Packet.Node.Core.Console;

namespace Packet.Node.Tests.Beacons;

/// <summary>
/// The beacon config model: YAML round-trip (system default + per-port override),
/// validation (default-off, interval ≥ 1, non-empty text when enabled), the
/// per-port/default merge (<see cref="EffectiveBeacon.Resolve"/>), and the shared
/// {node}/{call} text expansion the beacon reuses from the services banner.
/// </summary>
[Trait("Category", "Node")]
public sealed class BeaconConfigTests
{
    private static readonly NodeConfigValidator Validator = new();

    private static NodeConfig WithBeacon(BeaconConfig beacon, PortBeaconConfig? portBeacon = null) => new()
    {
        Identity = new Identity { Callsign = "M0LTE-1" },
        Ports =
        [
            new PortConfig
            {
                Id = "vhf",
                Transport = new KissTcpTransport { Host = "127.0.0.1", Port = 8001 },
                Beacon = portBeacon,
            }
        ],
        Beacon = beacon,
    };

    // ─── YAML round-trip ──────────────────────────────────────────────────

    [Fact]
    public void Round_trips_the_system_default_beacon_and_a_per_port_override()
    {
        var original = WithBeacon(
            new BeaconConfig { Enabled = true, IntervalMinutes = 20, Text = "{node} pdn node" },
            new PortBeaconConfig { Enabled = true, IntervalMinutes = 5, Text = "{call} on VHF" });

        var yaml = NodeConfigYaml.Serialize(original);
        var reparsed = NodeConfigYaml.Parse(yaml);

        reparsed.Beacon.Enabled.Should().BeTrue();
        reparsed.Beacon.IntervalMinutes.Should().Be(20);
        reparsed.Beacon.Text.Should().Be("{node} pdn node");

        var port = reparsed.Ports.Single();
        port.Beacon.Should().NotBeNull();
        port.Beacon!.Enabled.Should().BeTrue();
        port.Beacon.IntervalMinutes.Should().Be(5);
        port.Beacon.Text.Should().Be("{call} on VHF");
    }

    [Fact]
    public void A_port_with_no_beacon_block_round_trips_as_null_override()
    {
        var original = WithBeacon(new BeaconConfig { Enabled = true, IntervalMinutes = 30, Text = "x" });

        var reparsed = NodeConfigYaml.Parse(NodeConfigYaml.Serialize(original));

        reparsed.Ports.Single().Beacon.Should().BeNull("no per-port override = inherit the default");
    }

    [Fact]
    public void An_absent_beacon_key_defaults_to_disabled()
    {
        // A config that predates beacons (no beacon: key) parses with the default-OFF beacon.
        const string yaml = """
            schemaVersion: 1
            identity:
              callsign: M0LTE-1
            ports: []
            """;

        var cfg = NodeConfigYaml.Parse(yaml);

        cfg.Beacon.Should().NotBeNull();
        cfg.Beacon.Enabled.Should().BeFalse("default-off — the no-regression contract");
        cfg.Beacon.IntervalMinutes.Should().Be(30);
        cfg.Beacon.Text.Should().Be("{node} pdn node");
    }

    [Fact]
    public void The_default_beacon_is_disabled()
    {
        new BeaconConfig().Enabled.Should().BeFalse();
        new NodeConfig { Identity = new Identity { Callsign = "M0LTE-1" } }.Beacon.Enabled.Should().BeFalse();
    }

    // ─── validation ───────────────────────────────────────────────────────

    [Fact]
    public void Accepts_a_default_disabled_beacon_even_with_empty_text()
    {
        // Disabled ⇒ text is inert, so an empty text is not an error.
        Validator.Validate(WithBeacon(new BeaconConfig { Enabled = false, Text = "" })).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Accepts_an_enabled_beacon_with_text_and_a_sane_interval()
    {
        Validator.Validate(WithBeacon(new BeaconConfig { Enabled = true, IntervalMinutes = 1, Text = "hi" }))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Rejects_a_system_default_interval_below_one_minute(int minutes)
    {
        Validator.Validate(WithBeacon(new BeaconConfig { Enabled = true, IntervalMinutes = minutes, Text = "x" }))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_an_enabled_beacon_with_empty_text()
    {
        Validator.Validate(WithBeacon(new BeaconConfig { Enabled = true, IntervalMinutes = 30, Text = "" }))
            .IsValid.Should().BeFalse();
        Validator.Validate(WithBeacon(new BeaconConfig { Enabled = true, IntervalMinutes = 30, Text = "   " }))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_a_per_port_override_interval_below_one_minute()
    {
        Validator.Validate(WithBeacon(
            new BeaconConfig { Enabled = true, IntervalMinutes = 30, Text = "x" },
            new PortBeaconConfig { Enabled = true, IntervalMinutes = 0 }))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_an_enabled_per_port_override_with_empty_text_when_set()
    {
        Validator.Validate(WithBeacon(
            new BeaconConfig { Enabled = true, IntervalMinutes = 30, Text = "x" },
            new PortBeaconConfig { Enabled = true, Text = "" }))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Accepts_a_per_port_override_with_null_inherit_fields()
    {
        Validator.Validate(WithBeacon(
            new BeaconConfig { Enabled = true, IntervalMinutes = 30, Text = "x" },
            new PortBeaconConfig { Enabled = true, IntervalMinutes = null, Text = null }))
            .IsValid.Should().BeTrue();
    }

    // ─── effective merge ──────────────────────────────────────────────────

    [Fact]
    public void Resolve_with_no_override_returns_the_system_default_wholesale()
    {
        var def = new BeaconConfig { Enabled = true, IntervalMinutes = 30, Text = "{node}" };
        var eff = EffectiveBeacon.Resolve(def, port: null);
        eff.Should().Be(new EffectiveBeacon(true, 30, "{node}"));
    }

    [Fact]
    public void Resolve_merges_null_override_fields_from_the_default()
    {
        var def = new BeaconConfig { Enabled = false, IntervalMinutes = 30, Text = "default text" };
        var eff = EffectiveBeacon.Resolve(def, new PortBeaconConfig { Enabled = true, IntervalMinutes = null, Text = null });
        eff.Enabled.Should().BeTrue("the override's flag is authoritative");
        eff.IntervalMinutes.Should().Be(30);
        eff.Text.Should().Be("default text");
    }

    [Fact]
    public void Resolve_lets_the_override_win_field_by_field()
    {
        var def = new BeaconConfig { Enabled = true, IntervalMinutes = 30, Text = "default" };
        var eff = EffectiveBeacon.Resolve(def, new PortBeaconConfig { Enabled = false, IntervalMinutes = 5, Text = "custom" });
        eff.Should().Be(new EffectiveBeacon(false, 5, "custom"));
    }

    // ─── text expansion (shared with the services banner) ─────────────────

    [Theory]
    [InlineData("M0LTE-1", "LONDON", "{node} pdn node", "LONDON pdn node")]   // {node} = alias when set
    [InlineData("M0LTE-1", null, "{node} pdn node", "M0LTE-1 pdn node")]      // {node} = callsign when no alias
    [InlineData("M0LTE-1", "LONDON", "{call} de {node}", "M0LTE-1 de LONDON")]
    [InlineData("M0LTE-1", "LONDON", "no placeholders", "no placeholders")]
    public void Beacon_text_expands_node_and_call_like_the_banner(string callsign, string? alias, string template, string expected)
    {
        var nodeName = NodeTextTemplate.NodeName(callsign, alias);
        NodeTextTemplate.Expand(template, nodeName, callsign).Should().Be(expected);
    }
}
