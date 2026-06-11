using Packet.Node.Core.Configuration;

namespace Packet.Node.Core.Applications.Packages;

/// <summary>
/// Discovers app packages (<c>pdn-app.yaml</c> under the package roots), merges each manifest
/// with the owner's <c>apps:</c> override, and validates the result. The catalog is re-scanned
/// at startup and on every config apply — discovery is cheap and the result is a pure snapshot.
/// Contract: <c>docs/app-packages.md</c> § Discovery.
/// </summary>
public interface IAppPackageCatalog
{
    /// <summary>Scan the package roots (the config's <c>appPackageRoots:</c> when set, else
    /// the defaults) and return every discovered package merged with its override from
    /// <paramref name="config"/>. Unreadable or invalid manifests are returned as
    /// <see cref="DiscoveredAppPackage.Error"/> entries rather than thrown — the owner sees
    /// the problem in the UI instead of losing the whole inventory.</summary>
    IReadOnlyList<DiscoveredAppPackage> Discover(NodeConfig config);
}

/// <summary>One discovered package: the manifest, where it lives, and the owner-resolved state.</summary>
public sealed record DiscoveredAppPackage
{
    /// <summary>The parsed manifest. Null only when <see cref="Error"/> is set.</summary>
    public AppPackageManifest? Manifest { get; init; }

    /// <summary>The package id (from the manifest, or the directory name for an error entry).</summary>
    public required string Id { get; init; }

    /// <summary>Absolute package directory (where <c>pdn-app.yaml</c> was found).</summary>
    public required string PackageDir { get; init; }

    /// <summary>Absolute per-app state directory (<c>/var/lib/packetnet/apps/&lt;id&gt;</c> by
    /// convention; under the test/dev root when overridden).</summary>
    public required string StateDir { get; init; }

    /// <summary>The owner's resolved trust state — false unless an <c>apps:</c> entry enables it.</summary>
    public bool Enabled { get; init; }

    /// <summary>The owner's override entry, when present.</summary>
    public AppOverrideConfig? Override { get; init; }

    /// <summary>Human-readable manifest/validation problem; non-null marks the entry broken
    /// (broken entries are never enabled, sessions never resolve, services never start).</summary>
    public string? Error { get; init; }
}
