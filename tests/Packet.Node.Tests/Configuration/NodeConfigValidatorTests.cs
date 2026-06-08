using Packet.Node.Core.Configuration;
using Packet.NetRom.Wire;

namespace Packet.Node.Tests.Configuration;

public class NodeConfigValidatorTests
{
    private static readonly NodeConfigValidator Validator = new();

    private static NodeConfig Valid(params PortConfig[] ports) => new()
    {
        Identity = new Identity { Callsign = "M0LTE-1" },
        Ports = ports,
    };

    private static PortConfig TcpPort(string id, string host = "127.0.0.1", int port = 8001, bool enabled = true) => new()
    {
        Id = id,
        Enabled = enabled,
        Transport = new KissTcpTransport { Host = host, Port = port },
    };

    [Fact]
    public void Accepts_a_minimal_idle_node_with_no_ports()
    {
        Validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Accepts_a_node_with_a_valid_port()
    {
        Validator.Validate(Valid(TcpPort("vhf"))).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("M0LTE-1", true)]
    [InlineData("G7XYZ", true)]
    [InlineData("M0LTE-15", true)]
    [InlineData("M0LTE-16", false)]   // SSID out of 0..15
    [InlineData("TOOLONGCALL", false)]
    [InlineData("lower", false)]      // lowercase not allowed by Callsign
    [InlineData("", false)]
    public void Callsign_acceptance_pairs_with_Callsign_TryParse(string callsign, bool expectValid)
    {
        var config = new NodeConfig { Identity = new Identity { Callsign = callsign } };
        Validator.Validate(config).IsValid.Should().Be(expectValid);
    }

    [Fact]
    public void Rejects_duplicate_port_ids_but_accepts_distinct_ones()
    {
        Validator.Validate(Valid(TcpPort("dup", port: 1), TcpPort("dup", port: 2)))
            .IsValid.Should().BeFalse();
        Validator.Validate(Valid(TcpPort("a", port: 1), TcpPort("b", port: 2)))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_two_ports_on_the_same_endpoint_but_accepts_distinct_endpoints()
    {
        // Same host:port on two ports — a device collision.
        Validator.Validate(Valid(TcpPort("a", "10.0.0.1", 9000), TcpPort("b", "10.0.0.1", 9000)))
            .IsValid.Should().BeFalse();
        Validator.Validate(Valid(TcpPort("a", "10.0.0.1", 9000), TcpPort("b", "10.0.0.1", 9001)))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, false)]    // baud must be > 0
    [InlineData(1, true)]
    [InlineData(57600, true)]
    public void Serial_baud_must_be_positive(int baud, bool expectValid)
    {
        var config = Valid(new PortConfig
        {
            Id = "s",
            Transport = new SerialKissTransport { Device = "/dev/ttyACM0", Baud = baud },
        });
        Validator.Validate(config).IsValid.Should().Be(expectValid);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(15, true)]
    [InlineData(16, false)]
    public void Nino_mode_must_be_in_0_to_15(int mode, bool expectValid)
    {
        var config = Valid(new PortConfig
        {
            Id = "n",
            Transport = new NinoTncTransport { Device = "/dev/ttyACM0", Baud = 57600, Mode = mode },
        });
        Validator.Validate(config).IsValid.Should().Be(expectValid);
    }

    [Theory]
    [InlineData(0, false)]      // port out of 1..65535
    [InlineData(1, true)]
    [InlineData(65535, true)]
    [InlineData(70000, false)]
    public void KissTcp_port_must_be_in_range(int port, bool expectValid)
    {
        var config = Valid(new PortConfig
        {
            Id = "t",
            Transport = new KissTcpTransport { Host = "h", Port = port },
        });
        Validator.Validate(config).IsValid.Should().Be(expectValid);
    }

    [Theory]
    [InlineData(0, false)]       // remote port out of 1..65535
    [InlineData(1, true)]
    [InlineData(10093, true)]
    [InlineData(65535, true)]
    [InlineData(70000, false)]
    public void Axudp_remote_port_must_be_in_range(int port, bool expectValid)
    {
        var config = Valid(new PortConfig
        {
            Id = "a",
            Transport = new AxudpTransport { Host = "peer", Port = port, LocalPort = 10093 },
        });
        Validator.Validate(config).IsValid.Should().Be(expectValid);
    }

    [Theory]
    [InlineData(-1, false)]      // localPort 0 is legal (ephemeral); negative is not
    [InlineData(0, true)]
    [InlineData(10093, true)]
    [InlineData(70000, false)]
    public void Axudp_localPort_allows_zero_ephemeral_but_must_be_in_range(int localPort, bool expectValid)
    {
        var config = Valid(new PortConfig
        {
            Id = "a",
            Transport = new AxudpTransport { Host = "peer", Port = 10093, LocalPort = localPort },
        });
        Validator.Validate(config).IsValid.Should().Be(expectValid);
    }

    [Fact]
    public void Axudp_requires_a_host()
    {
        var config = Valid(new PortConfig
        {
            Id = "a",
            Transport = new AxudpTransport { Host = "", Port = 10093 },
        });
        Validator.Validate(config).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, true)]              // no profile = spec defaults = valid
    [InlineData("", true)]               // blank = no profile = valid
    [InlineData("slow-afsk1200", true)]  // the known profile
    [InlineData("SLOW_AFSK1200", true)]  // case- + separator-insensitive
    [InlineData("turbo", false)]         // unknown profile = config error
    public void Profile_must_be_a_known_name_or_absent(string? profile, bool expectValid)
    {
        var config = Valid(new PortConfig
        {
            Id = "p",
            Profile = profile,
            Transport = new KissTcpTransport { Host = "h", Port = 1 },
        });
        Validator.Validate(config).IsValid.Should().Be(expectValid);
    }

    [Fact]
    public void Rejects_out_of_range_ax25_window()
    {
        var bad = Valid(new PortConfig
        {
            Id = "p",
            Transport = new KissTcpTransport { Host = "h", Port = 1 },
            Ax25 = new Ax25PortParams { WindowSize = 200 },
        });
        Validator.Validate(bad).IsValid.Should().BeFalse();

        var ok = Valid(new PortConfig
        {
            Id = "p",
            Transport = new KissTcpTransport { Host = "h", Port = 1 },
            Ax25 = new Ax25PortParams { WindowSize = 7 },
        });
        Validator.Validate(ok).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_telnet_port_out_of_range()
    {
        var config = Valid() with
        {
            Management = new ManagementConfig { Telnet = new TelnetConfig { Port = 0 } },
        };
        Validator.Validate(config).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Accepts_netrom_knobs_in_range_and_rejects_out_of_range()
    {
        var ok = Valid() with
        {
            NetRom = new NetRomConfig
            {
                Enabled = true,
                DefaultNeighbourQuality = 203,
                MinQuality = 150,
                ObsoleteInitial = 6,
                SweepIntervalSeconds = 1800,
            },
        };
        Validator.Validate(ok).IsValid.Should().BeTrue();

        Validator.Validate(Valid() with { NetRom = new NetRomConfig { MinQuality = 256 } })
            .IsValid.Should().BeFalse("quality must be in 0..255");
        Validator.Validate(Valid() with { NetRom = new NetRomConfig { ObsoleteInitial = 0 } })
            .IsValid.Should().BeFalse("OBSINIT must be positive");
        Validator.Validate(Valid() with { NetRom = new NetRomConfig { SweepIntervalSeconds = -1 } })
            .IsValid.Should().BeFalse("sweep interval must be positive");
    }

    [Fact]
    public void Accepts_netrom_l3l4_knobs_in_range_and_rejects_out_of_range()
    {
        var ok = Valid() with
        {
            NetRom = new NetRomConfig
            {
                Enabled = true, Broadcast = true, Connect = true, Alias = "NODE",
                ObsoleteMinimum = 4, Window = 7, TransportTimeoutSeconds = 8,
                TransportRetries = 5, TimeToLive = 30,
            },
        };
        Validator.Validate(ok).IsValid.Should().BeTrue();

        Validator.Validate(Valid() with { NetRom = new NetRomConfig { Window = 0 } })
            .IsValid.Should().BeFalse("window must be in 1..127");
        Validator.Validate(Valid() with { NetRom = new NetRomConfig { Window = 200 } })
            .IsValid.Should().BeFalse("window must be in 1..127 (8-bit sequence space)");
        Validator.Validate(Valid() with { NetRom = new NetRomConfig { TimeToLive = 0 } })
            .IsValid.Should().BeFalse("TTL must be in 1..255");
        Validator.Validate(Valid() with { NetRom = new NetRomConfig { TransportRetries = 0 } })
            .IsValid.Should().BeFalse("retries must be positive");
        Validator.Validate(Valid() with { NetRom = new NetRomConfig { Enabled = false, Broadcast = true } })
            .IsValid.Should().BeFalse("broadcast requires the service enabled");
        Validator.Validate(Valid() with { NetRom = new NetRomConfig { Enabled = false, Connect = true } })
            .IsValid.Should().BeFalse("connect requires the service enabled");
    }

    [Fact]
    public void Default_netrom_inp3_overlay_validates_disabled()
    {
        // The default-off proof at the validator: a config with no inp3: overrides
        // (Inp3 == NetRomInp3Options.Default ⇒ Enabled == false) validates fine.
        Validator.Validate(Valid()).IsValid.Should().BeTrue();
        Valid().NetRom.Inp3.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Accepts_a_valid_enabled_inp3_overlay()
    {
        var ok = Valid() with
        {
            NetRom = new NetRomConfig
            {
                Enabled = true,
                Connect = true,   // INP3 rides the connected-mode interlink
                Inp3 = new NetRomInp3Options
                {
                    Enabled = true,
                    L3RttInterval = TimeSpan.FromSeconds(60),
                    L3RttResetWindow = TimeSpan.FromSeconds(180),
                    RifInterval = TimeSpan.FromSeconds(300),
                    PositiveDebounce = TimeSpan.FromSeconds(5),
                    SnttGainShift = 3,
                    HopLimit = 30,
                },
            },
        };
        Validator.Validate(ok).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_out_of_range_inp3_values_via_the_records_own_Validate()
    {
        // A simple scalar out of range (SnttGainShift valid 1..8).
        Validator.Validate(Valid() with
        {
            NetRom = new NetRomConfig { Enabled = true, Inp3 = new NetRomInp3Options { SnttGainShift = 0 } },
        }).IsValid.Should().BeFalse("snttGainShift must be in 1..8");

        // The HopLimit floor.
        Validator.Validate(Valid() with
        {
            NetRom = new NetRomConfig { Enabled = true, Inp3 = new NetRomInp3Options { HopLimit = 0 } },
        }).IsValid.Should().BeFalse("hopLimit must be at least 1");

        // Cross-field: the reset window must exceed the probe interval.
        Validator.Validate(Valid() with
        {
            NetRom = new NetRomConfig
            {
                Enabled = true,
                Inp3 = new NetRomInp3Options
                {
                    L3RttInterval = TimeSpan.FromSeconds(180),
                    L3RttResetWindow = TimeSpan.FromSeconds(60),
                },
            },
        }).IsValid.Should().BeFalse("l3RttResetWindow must exceed l3RttInterval");

        // Cross-field: the positive debounce must be strictly less than the RIF interval.
        Validator.Validate(Valid() with
        {
            NetRom = new NetRomConfig
            {
                Enabled = true,
                Inp3 = new NetRomInp3Options
                {
                    RifInterval = TimeSpan.FromSeconds(5),
                    PositiveDebounce = TimeSpan.FromSeconds(5),
                },
            },
        }).IsValid.Should().BeFalse("positiveDebounce must be < rifInterval");
    }

    [Fact]
    public void Inp3_enabled_requires_netrom_enabled()
    {
        // The cross-field guard mirroring broadcast/connect: an overlay on a deaf
        // node is meaningless.
        Validator.Validate(Valid() with
        {
            NetRom = new NetRomConfig { Enabled = false, Connect = true, Inp3 = new NetRomInp3Options { Enabled = true } },
        }).IsValid.Should().BeFalse("inp3.enabled requires netrom.enabled");

        Validator.Validate(Valid() with
        {
            NetRom = new NetRomConfig { Enabled = true, Connect = true, Inp3 = new NetRomInp3Options { Enabled = true } },
        }).IsValid.Should().BeTrue("inp3.enabled with netrom.enabled + connect is fine");
    }

    [Fact]
    public void Inp3_enabled_requires_netrom_connect()
    {
        // INP3 rides the connected-mode interlink machinery, so the host only constructs the
        // overlay under Connect. Without this guard, inp3.enabled + connect:false would validate
        // and then silently no-op — reject it explicitly (the named-flag discipline).
        Validator.Validate(Valid() with
        {
            NetRom = new NetRomConfig { Enabled = true, Connect = false, Inp3 = new NetRomInp3Options { Enabled = true } },
        }).IsValid.Should().BeFalse("inp3.enabled requires netrom.connect");

        Validator.Validate(Valid() with
        {
            NetRom = new NetRomConfig { Enabled = true, Connect = true, Inp3 = new NetRomInp3Options { Enabled = true } },
        }).IsValid.Should().BeTrue("inp3.enabled with netrom.connect is fine");
    }
}
