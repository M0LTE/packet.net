using Packet.Node.Core.Applications;
using Packet.Node.Core.Applications.Packages;
using Packet.Node.Core.Configuration;
using Packet.Node.Tests.Support;

namespace Packet.Node.Tests.Applications.Packages;

/// <summary>
/// The session-resolution union in <see cref="ApplicationHost"/>: verbs resolve from the inline
/// <c>applications:</c> list first, then from enabled, error-free app packages with a
/// <c>session:</c> block — mapped onto the <see cref="ApplicationConfig"/> shape the existing
/// run path understands (command/args resolved against the package dir, working dir = the
/// state dir, capabilities from the manifest, no UI). A null catalog is exactly the
/// pre-package host, which the existing <c>ApplicationHostTests</c> keep covering.
/// </summary>
[Trait("Category", "Node")]
public sealed class ApplicationHostPackageUnionTests
{
    private static NodeConfig Cfg(params ApplicationConfig[] inline) => new()
    {
        Identity = new Identity { Callsign = "M0LTE-1" },
        Applications = inline,
    };

    private static ApplicationHost Host(FakeAppPackageCatalog catalog, params ApplicationConfig[] inline) =>
        new(new TestConfigProvider(Cfg(inline)), loggerFactory: null, catalog);

    private static AppPackageManifest SessionManifest(
        string id,
        string match = "LOBBY",
        ApplicationKind kind = ApplicationKind.Process,
        string? command = "/usr/bin/python3",
        IReadOnlyList<string>? args = null,
        string? socketPath = null,
        IReadOnlyList<string>? capabilities = null) => new()
        {
            Manifest = 1,
            Id = id,
            Capabilities = capabilities ?? ["session"],
            Session = new AppSessionSpec
            {
                Match = match,
                Kind = kind,
                Command = command,
                Args = args ?? [],
                SocketPath = socketPath,
            },
        };

    [Fact]
    public void Package_session_resolves_and_maps_to_the_run_shape()
    {
        using var pkg = new TempAppPackage("lobby");
        var script = pkg.WriteScript("lobby.py", "# the app\n");
        var catalog = new FakeAppPackageCatalog();
        catalog.Set(pkg.Discovered(SessionManifest("lobby", args: ["lobby.py", "--flag"])));
        var host = Host(catalog);

        var resolved = host.Resolve("lobby");   // case-insensitive, like inline verbs

        Assert.NotNull(resolved);
        Assert.Equal("lobby", resolved!.Id);
        Assert.Equal("LOBBY", resolved.Match);
        Assert.True(resolved.Enabled);
        Assert.Equal(ApplicationKind.Process, resolved.Kind);
        Assert.Equal("/usr/bin/python3", resolved.Command);   // absolute → untouched
        Assert.Equal(script, resolved.Args[0]);               // names a package file → absolute
        Assert.Equal("--flag", resolved.Args[1]);             // a flag passes through
        Assert.Equal(pkg.StateDir, resolved.WorkingDirectory);
        Assert.True(Directory.Exists(pkg.StateDir), "the state dir is created on first use");
        Assert.Equal("session", Assert.Single(resolved.Capabilities));
        Assert.Null(resolved.Ui);   // tiles are the gateway's concern
    }

    [Fact]
    public void Relative_command_naming_a_package_file_resolves_against_the_package_dir()
    {
        using var pkg = new TempAppPackage("script");
        var script = pkg.WriteScript("run.sh", "#!/bin/sh\n");
        var catalog = new FakeAppPackageCatalog();
        catalog.Set(pkg.Discovered(SessionManifest("script", match: "RUN", command: "run.sh")));
        var host = Host(catalog);

        Assert.Equal(script, host.Resolve("RUN")!.Command);
    }

    [Fact]
    public void Disabled_package_does_not_resolve()
    {
        using var pkg = new TempAppPackage("lobby");
        var catalog = new FakeAppPackageCatalog();
        catalog.Set(pkg.Discovered(SessionManifest("lobby"), enabled: false));

        Assert.Null(Host(catalog).Resolve("LOBBY"));
    }

    [Fact]
    public void Broken_package_does_not_resolve()
    {
        using var pkg = new TempAppPackage("lobby");
        var catalog = new FakeAppPackageCatalog();
        catalog.Set(pkg.Discovered(SessionManifest("lobby"), error: "manifest invalid: id mismatch"));

        Assert.Null(Host(catalog).Resolve("LOBBY"));
    }

    [Fact]
    public void Package_without_a_session_block_does_not_resolve()
    {
        using var pkg = new TempAppPackage("daemon");
        pkg.WriteScript("run.sh", "#!/bin/sh\n");
        var catalog = new FakeAppPackageCatalog();
        catalog.Set(pkg.Service("run.sh"));   // service-only manifest — no console verb

        Assert.Null(Host(catalog).Resolve("DAEMON"));
    }

    [Fact]
    public void Inline_application_beats_a_package_on_the_same_verb()
    {
        using var pkg = new TempAppPackage("lobby");
        var catalog = new FakeAppPackageCatalog();
        catalog.Set(pkg.Discovered(SessionManifest("lobby")));
        var inline = new ApplicationConfig { Id = "inline-lobby", Match = "LOBBY", Command = "/bin/cat" };
        var host = Host(catalog, inline);

        Assert.Equal("inline-lobby", host.Resolve("LOBBY")!.Id);
    }

    [Fact]
    public void Owner_match_override_replaces_the_manifest_verb()
    {
        using var pkg = new TempAppPackage("lobby");
        var catalog = new FakeAppPackageCatalog();
        catalog.Set(pkg.Discovered(
            SessionManifest("lobby"),
            @override: new AppOverrideConfig { Id = "lobby", Enabled = true, Match = "FOYER" }));
        var host = Host(catalog);

        Assert.Equal("lobby", host.Resolve("FOYER")!.Id);
        Assert.Equal("FOYER", host.Resolve("foyer")!.Match);
        Assert.Null(host.Resolve("LOBBY"));   // the overridden verb is gone
    }

    [Fact]
    public void Socket_kind_session_maps_socket_path_through()
    {
        using var pkg = new TempAppPackage("lobby");
        var catalog = new FakeAppPackageCatalog();
        catalog.Set(pkg.Discovered(SessionManifest(
            "lobby", kind: ApplicationKind.Socket, command: null, socketPath: "/run/packetnet/lobby.sock")));
        var host = Host(catalog);

        var resolved = host.Resolve("LOBBY");
        Assert.NotNull(resolved);
        Assert.Equal(ApplicationKind.Socket, resolved!.Kind);
        Assert.Equal("/run/packetnet/lobby.sock", resolved.SocketPath);
        Assert.Null(resolved.Command);
    }

    [Fact]
    public void Package_resolution_reads_the_catalog_live()
    {
        using var pkg = new TempAppPackage("lobby");
        var catalog = new FakeAppPackageCatalog();
        catalog.Set(pkg.Discovered(SessionManifest("lobby"), enabled: false));
        var host = Host(catalog);
        Assert.Null(host.Resolve("LOBBY"));

        catalog.Set(pkg.Discovered(SessionManifest("lobby"), enabled: true));   // the owner enables it
        Assert.Equal("lobby", host.Resolve("LOBBY")!.Id);
    }
}
