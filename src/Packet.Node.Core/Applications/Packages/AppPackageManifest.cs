using Packet.Node.Core.Configuration;

namespace Packet.Node.Core.Applications.Packages;

/// <summary>
/// An app package's <c>pdn-app.yaml</c> — the manifest <b>authored by the app</b>, not the node
/// owner (the owner's surface is the <c>apps:</c> override list, <see cref="AppOverrideConfig"/>).
/// The full contract is <c>docs/app-packages.md</c>; this record tree is its C# shape. At least
/// one of <see cref="Session"/> / <see cref="Service"/> / <see cref="Ui"/> must be present
/// (validated by the catalog).
/// </summary>
public sealed record AppPackageManifest
{
    /// <summary>Manifest schema version. Only <c>1</c> is understood; anything else is a
    /// validation error (forward-incompatible by design).</summary>
    public int Manifest { get; init; }

    /// <summary>Stable app identity — lowercase <c>[a-z0-9-]</c>, MUST equal the package
    /// directory name. The reconcile / log / override key.</summary>
    public required string Id { get; init; }

    /// <summary>Human label (UI tiles, the management list). Default: <see cref="Id"/>.</summary>
    public string? Name { get; init; }

    /// <summary>Informational version string, shown in the UI. Not parsed.</summary>
    public string? Version { get; init; }

    /// <summary>One-line human description, shown in the management list.</summary>
    public string? Description { get; init; }

    /// <summary>Optional lucide icon name (cosmetic).</summary>
    public string? Icon { get; init; }

    /// <summary>Declared capabilities — shown to the owner at enable time (the trust prompt),
    /// not enforced in v1. Conventional values: <c>session</c>, <c>network</c>, <c>web</c>.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>Packet-plane console attachment (the <c>pdn-app/1</c> wire) — optional.</summary>
    public AppSessionSpec? Session { get; init; }

    /// <summary>A long-running daemon pdn supervises (or observes, when
    /// <see cref="AppServiceSpec.Managed"/> is <see cref="AppServiceManaged.External"/>) — optional.</summary>
    public AppServiceSpec? Service { get; init; }

    /// <summary>Human-plane web UI (the existing app-gateway contract) — optional.</summary>
    public AppUiConfig? Ui { get; init; }
}

/// <summary>The manifest's <c>session:</c> block — how a console verb attaches a user session
/// to the app over the <c>pdn-app/1</c> wire. Mirrors the inline
/// <see cref="ApplicationConfig"/> fields, minus owner-only concerns.</summary>
public sealed record AppSessionSpec
{
    /// <summary>The console verb (e.g. <c>"LOBBY"</c>). Owner-overridable via
    /// <see cref="AppOverrideConfig.Match"/>. Same uniqueness/built-in-collision rules as
    /// inline applications.</summary>
    public required string Match { get; init; }

    /// <summary>Transport of the wire: spawn-per-connect <see cref="ApplicationKind.Process"/>
    /// or connect-per-session <see cref="ApplicationKind.Socket"/>.</summary>
    public ApplicationKind Kind { get; init; } = ApplicationKind.Process;

    /// <summary>Executable for <see cref="ApplicationKind.Process"/> — absolute, or relative to
    /// the package directory.</summary>
    public string? Command { get; init; }

    /// <summary>Arguments; an element naming an existing file relative to the package dir
    /// resolves to its absolute path (see the contract's path-resolution rule).</summary>
    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>The Unix-domain socket for <see cref="ApplicationKind.Socket"/>.</summary>
    public string? SocketPath { get; init; }
}

/// <summary>Restart policy for a supervised service.</summary>
public enum AppServiceRestart
{
    /// <summary>Restart (with backoff) only when the process exits non-zero or crashes.</summary>
    OnFailure,

    /// <summary>Restart (with backoff) on any exit.</summary>
    Always,

    /// <summary>Never restart automatically; any exit leaves the service stopped.</summary>
    Never,
}

/// <summary>Who owns the daemon's lifecycle.</summary>
public enum AppServiceManaged
{
    /// <summary>pdn starts, supervises, restarts, and stops the daemon (the default).</summary>
    Pdn,

    /// <summary>The owner runs the daemon (systemd etc.); pdn never starts or stops it and
    /// reports its state as <see cref="AppServiceState.External"/>.</summary>
    External,
}

/// <summary>The manifest's <c>service:</c> block — a long-running daemon.</summary>
public sealed record AppServiceSpec
{
    /// <summary>Executable — absolute, or relative to the package directory.</summary>
    public required string Command { get; init; }

    /// <summary>Arguments, package-dir-relative resolution as for the session block.</summary>
    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>Manifest-declared environment, merged UNDER the owner's
    /// <see cref="AppOverrideConfig.Environment"/> (owner wins), both over the
    /// <c>PDN_APP_*</c> variables the supervisor injects.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Working directory; null = the app's state dir
    /// (<c>/var/lib/packetnet/apps/&lt;id&gt;</c>).</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Restart policy. Default <see cref="AppServiceRestart.OnFailure"/>.</summary>
    public AppServiceRestart Restart { get; init; } = AppServiceRestart.OnFailure;

    /// <summary>Lifecycle ownership. Default <see cref="AppServiceManaged.Pdn"/>.</summary>
    public AppServiceManaged Managed { get; init; } = AppServiceManaged.Pdn;
}
