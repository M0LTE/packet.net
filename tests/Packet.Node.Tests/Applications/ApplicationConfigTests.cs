using Packet.Node.Core.Configuration;

namespace Packet.Node.Tests.Applications;

/// <summary>
/// The <c>applications:</c> registry binds from YAML and round-trips, and the validator
/// enforces its invariants: unique ids + match verbs, a process app needs a command, and a
/// match may not shadow a built-in console verb (so a registered app is never dead config).
/// </summary>
[Trait("Category", "Node")]
public sealed class ApplicationConfigTests
{
    private const string BaseIdentity = "identity:\n  callsign: M0LTE-1\n  alias: PDN\n";

    private static NodeConfig Valid(params ApplicationConfig[] apps) => new()
    {
        Identity = new Identity { Callsign = "M0LTE-1", Alias = "PDN" },
        Applications = apps,
    };

    private static FluentValidation.Results.ValidationResult Validate(NodeConfig cfg)
        => new NodeConfigValidator().Validate(cfg);

    [Fact]
    public void Applications_bind_from_yaml_with_args_and_default_kind()
    {
        var yaml = BaseIdentity + """
            applications:
              - id: wall
                command: WALL
                executable: /usr/bin/python3
                args: [ /usr/share/packetnet/apps/wall/wall.py ]
                workingDirectory: /var/lib/packetnet/apps/wall
                capabilities: [ session ]
            """;

        var cfg = NodeConfigYaml.Parse(yaml);

        var app = Assert.Single(cfg.Applications);
        Assert.Equal("wall", app.Id);
        Assert.Equal("WALL", app.Command);
        Assert.True(app.Enabled);                       // defaults true
        Assert.Equal(ApplicationKind.Process, app.Kind); // defaults Process when kind: omitted
        Assert.Equal("/usr/bin/python3", app.Executable);
        Assert.Equal(["/usr/share/packetnet/apps/wall/wall.py"], app.Args);
        Assert.Equal("/var/lib/packetnet/apps/wall", app.WorkingDirectory);
        Assert.Equal(["session"], app.Capabilities);
    }

    [Fact]
    public void Explicit_kind_and_disabled_bind()
    {
        var yaml = BaseIdentity + """
            applications:
              - id: wall
                command: WALL
                enabled: false
                kind: process
                executable: /bin/cat
            """;

        var app = Assert.Single(NodeConfigYaml.Parse(yaml).Applications);
        Assert.False(app.Enabled);
        Assert.Equal(ApplicationKind.Process, app.Kind);
    }

    [Fact]
    public void Applications_round_trip_through_serialize_parse()
    {
        var cfg = Valid(new ApplicationConfig
        {
            Id = "wall",
            Command = "WALL",
            Executable = "/usr/bin/python3",
            Args = ["wall.py"],
            Capabilities = ["session"],
        });

        var round = NodeConfigYaml.Parse(NodeConfigYaml.Serialize(cfg));

        var app = Assert.Single(round.Applications);
        Assert.Equal("wall", app.Id);
        Assert.Equal("WALL", app.Command);
        Assert.Equal("/usr/bin/python3", app.Executable);
        Assert.Equal(["wall.py"], app.Args);
    }

    [Fact]
    public void Empty_applications_is_the_default_and_valid()
    {
        var cfg = Valid();
        Assert.Empty(cfg.Applications);
        Assert.True(Validate(cfg).IsValid);
    }

    [Fact]
    public void A_well_formed_process_app_validates()
    {
        var cfg = Valid(new ApplicationConfig { Id = "wall", Command = "WALL", Executable = "/usr/bin/python3" });
        Assert.True(Validate(cfg).IsValid);
    }

    [Fact]
    public void Duplicate_ids_are_rejected()
    {
        var cfg = Valid(
            new ApplicationConfig { Id = "wall", Command = "WALL", Executable = "/bin/cat" },
            new ApplicationConfig { Id = "wall", Command = "GUEST", Executable = "/bin/cat" });
        Assert.False(Validate(cfg).IsValid);
    }

    [Theory]
    [InlineData("WALL", "wall")]   // same verb, different case
    [InlineData("WALL", "WALL")]
    public void Duplicate_match_verbs_are_rejected_case_insensitively(string a, string b)
    {
        var cfg = Valid(
            new ApplicationConfig { Id = "a", Command = a, Executable = "/bin/cat" },
            new ApplicationConfig { Id = "b", Command = b, Executable = "/bin/cat" });
        Assert.False(Validate(cfg).IsValid);
    }

    [Theory]
    [InlineData("BYE")]
    [InlineData("B")]        // an abbreviation of a built-in
    [InlineData("connect")]
    [InlineData("N")]        // Nodes
    [InlineData("?")]        // help
    [InlineData("SYSOP")]
    public void A_match_that_collides_with_a_builtin_verb_is_rejected(string match)
    {
        var cfg = Valid(new ApplicationConfig { Id = "x", Command = match, Executable = "/bin/cat" });
        Assert.False(Validate(cfg).IsValid);
    }

    [Fact]
    public void A_process_app_without_a_command_is_rejected()
    {
        var cfg = Valid(new ApplicationConfig { Id = "wall", Command = "WALL", Executable = null });
        Assert.False(Validate(cfg).IsValid);
    }

    [Fact]
    public void A_blank_match_is_rejected()
    {
        var cfg = Valid(new ApplicationConfig { Id = "wall", Command = "", Executable = "/bin/cat" });
        Assert.False(Validate(cfg).IsValid);
    }

    // ── The human-plane ui block (Slice 3) ──────────────────────────────

    [Fact]
    public void Ui_block_binds_from_yaml()
    {
        var yaml = BaseIdentity + """
            applications:
              - id: wall
                command: WALL
                executable: /usr/bin/python3
                ui:
                  upstream: http://127.0.0.1:9090
                  name: WALL
                  icon: message-square
            """;

        var app = Assert.Single(NodeConfigYaml.Parse(yaml).Applications);
        Assert.NotNull(app.Ui);
        Assert.Equal("http://127.0.0.1:9090", app.Ui!.Upstream);
        Assert.Equal("WALL", app.Ui.Name);
        Assert.Equal("message-square", app.Ui.Icon);
    }

    [Fact]
    public void A_well_formed_ui_app_validates()
    {
        var cfg = Valid(new ApplicationConfig
        {
            Id = "wall",
            Command = "WALL",
            Executable = "/bin/cat",
            Ui = new AppUiConfig { Upstream = "http://127.0.0.1:9090", Name = "WALL" },
        });
        Assert.True(Validate(cfg).IsValid);
    }

    // ── The socket rung (Slice 2) ───────────────────────────────────────

    [Fact]
    public void A_socket_app_binds_from_yaml_and_validates()
    {
        var yaml = BaseIdentity + """
            applications:
              - id: lobby
                command: LOBBY
                kind: socket
                socketPath: /run/packetnet/lobby.sock
            """;

        var app = Assert.Single(NodeConfigYaml.Parse(yaml).Applications);
        Assert.Equal(ApplicationKind.Socket, app.Kind);
        Assert.Equal("/run/packetnet/lobby.sock", app.SocketPath);
        Assert.True(Validate(new NodeConfig
        {
            Identity = new Identity { Callsign = "M0LTE-1" },
            Applications = [app],
        }).IsValid);
    }

    [Fact]
    public void A_socket_app_without_a_socket_path_is_rejected()
    {
        var cfg = Valid(new ApplicationConfig { Id = "lobby", Command = "LOBBY", Kind = ApplicationKind.Socket, SocketPath = null });
        Assert.False(Validate(cfg).IsValid);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("127.0.0.1:9090")]   // no scheme
    [InlineData("ftp://127.0.0.1")]  // wrong scheme
    [InlineData("")]
    public void An_invalid_ui_upstream_is_rejected(string upstream)
    {
        var cfg = Valid(new ApplicationConfig
        {
            Id = "wall",
            Command = "WALL",
            Executable = "/bin/cat",
            Ui = new AppUiConfig { Upstream = upstream },
        });
        Assert.False(Validate(cfg).IsValid);
    }
}
