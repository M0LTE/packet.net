using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Packet.Node.Core.Applications.Packages;
using Packet.Node.Core.Configuration;
using Packet.Node.Core.Console;

namespace Packet.Node.Core.Applications;

/// <summary>
/// Resolves and launches registered <see cref="ApplicationConfig"/> apps over a connected
/// session. The node console (application #0) calls this when a user types a verb it doesn't
/// recognise: if the verb matches a registered, enabled app, the host runs it over the same
/// session and returns control to the console when the app exits.
/// </summary>
public interface IApplicationHost
{
    /// <summary>Resolve an <b>enabled</b> application whose <see cref="ApplicationConfig.Match"/>
    /// equals <paramref name="verb"/> (case-insensitive, exact — no abbreviation), reading the
    /// live config so a hot edit applies to the next launch. Resolution is the <b>union</b> of
    /// the inline <c>applications:</c> list and the enabled, error-free app packages with a
    /// <c>session:</c> block (<c>docs/app-packages.md</c>) — inline first on a verb tie. Null
    /// if none matches.</summary>
    ApplicationConfig? Resolve(string verb);

    /// <summary>Run <paramref name="app"/> over <paramref name="session"/> until it finishes or
    /// the user drops, then return so the console can re-prompt. Total: a spawn/run failure is
    /// reported to the user and logged, never thrown (other than cancellation).</summary>
    Task RunAsync(ApplicationConfig app, INodeConnection session, NodeAppContext context, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IApplicationHost"/>
public sealed partial class ApplicationHost : IApplicationHost
{
    private readonly IConfigProvider config;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<ApplicationHost> logger;
    // Optional app-package catalog (docs/app-packages.md). Null = inline applications only —
    // exactly the pre-package behaviour, so every existing caller/test is untouched.
    private readonly IAppPackageCatalog? catalog;

    public ApplicationHost(IConfigProvider config, ILoggerFactory? loggerFactory = null, IAppPackageCatalog? catalog = null)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        this.catalog = catalog;
        logger = this.loggerFactory.CreateLogger<ApplicationHost>();
    }

    /// <inheritdoc/>
    public ApplicationConfig? Resolve(string verb)
    {
        if (string.IsNullOrWhiteSpace(verb))
        {
            return null;
        }
        var wanted = verb.Trim();
        var current = config.Current;
        foreach (var app in current.Applications)
        {
            if (app.Enabled && !string.IsNullOrWhiteSpace(app.Match)
                && string.Equals(app.Match.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return app;
            }
        }
        return ResolvePackage(wanted, current);
    }

    /// <summary>The package half of the union: an enabled, error-free discovered package with a
    /// <c>session:</c> block whose effective verb (owner override ?? manifest) matches. Mapped
    /// to the <see cref="ApplicationConfig"/> shape the existing run path already understands —
    /// command/args resolved against the package dir, working dir = the app's state dir.</summary>
    private ApplicationConfig? ResolvePackage(string wanted, NodeConfig current)
    {
        if (catalog is null)
        {
            return null;
        }
        foreach (var pkg in catalog.Discover(current))
        {
            if (!pkg.Enabled || pkg.Error is not null || pkg.Manifest?.Session is not { } session)
            {
                continue;
            }
            var match = !string.IsNullOrWhiteSpace(pkg.Override?.Match) ? pkg.Override!.Match! : session.Match;
            if (string.IsNullOrWhiteSpace(match)
                || !string.Equals(match.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // First use of the app's state dir — make it exist (it is the session's working
            // directory). A failure is logged and left for the spawn to surface as
            // "unavailable" through the total RunAsync path.
            try
            {
                Directory.CreateDirectory(pkg.StateDir);
            }
            catch (Exception ex)
            {
                LogStateDirFailed(ex, pkg.Id, pkg.StateDir);
            }

            return new ApplicationConfig
            {
                Id = pkg.Id,
                Match = match.Trim(),
                Enabled = true,
                Kind = session.Kind,
                Command = session.Command is null
                    ? null
                    : AppPackagePaths.ResolveFile(session.Command, pkg.PackageDir),
                SocketPath = session.SocketPath,
                Args = session.Args.Select(a => AppPackagePaths.ResolveFile(a, pkg.PackageDir)).ToArray(),
                WorkingDirectory = pkg.StateDir,
                Capabilities = pkg.Manifest.Capabilities,
                Ui = null,   // tiles are the gateway's concern — out of the session union's scope
            };
        }
        return null;
    }

    /// <inheritdoc/>
    public async Task RunAsync(ApplicationConfig app, INodeConnection session, NodeAppContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);

        // CA1859: 'impl' is deliberately the INodeApplication abstraction, not the single
        // concrete type slice 1 happens to build — this is the platform seam, and later
        // ApplicationKinds (long-running socket, WASM) are dropped into the switch below. The
        // micro-perf of a concrete type is irrelevant against spawning a process per launch.
#pragma warning disable CA1859
        INodeApplication impl;
#pragma warning restore CA1859
        try
        {
            // The kind→implementation map. Slice 1 has one arm (out-of-process); later kinds
            // (long-running socket, WASM) are added here. Built per launch so config edits apply.
            impl = app.Kind switch
            {
                ApplicationKind.Process => new ExternalProcessApplication(app, loggerFactory.CreateLogger<ExternalProcessApplication>()),
                ApplicationKind.Socket => new SocketApplication(app, loggerFactory.CreateLogger<SocketApplication>()),
                _ => throw new NotSupportedException($"Unsupported application kind '{app.Kind}'."),
            };
        }
        catch (Exception ex)
        {
            LogBuildFailed(ex, app.Id);
            await WriteLineAsync(session, "That application is unavailable.", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            LogLaunching(app.Id, context.Callsign);
            await impl.RunAsync(session, context, cancellationToken).ConfigureAwait(false);
        }
        catch (ApplicationStartException ex)
        {
            LogStartFailed(ex, app.Id);
            await WriteLineAsync(session, "That application is unavailable.", cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;   // node/session shutting down — let the console loop unwind
        }
        catch (Exception ex)
        {
            LogRunFailed(ex, app.Id);
            await WriteLineAsync(session, "That application ended unexpectedly.", cancellationToken).ConfigureAwait(false);
        }
    }

    // Best-effort one-line write to the user, terminated per transport (CR for AX.25 /
    // NET-ROM, CR-LF for telnet) — matches the console's line discipline.
    private static async Task WriteLineAsync(INodeConnection connection, string text, CancellationToken ct)
    {
        var nl = connection.TransportKind == NodeTransportKind.Telnet ? "\r\n" : "\r";
        try
        {
            await connection.WriteAsync(Encoding.UTF8.GetBytes(text + nl), ct).ConfigureAwait(false);
        }
        catch
        {
            // The user is gone — nothing to report to.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Launching app '{Id}' for {Callsign}.")]
    private partial void LogLaunching(string id, string callsign);

    [LoggerMessage(Level = LogLevel.Warning, Message = "App '{Id}' could not be built.")]
    private partial void LogBuildFailed(Exception ex, string id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "App '{Id}' failed to start.")]
    private partial void LogStartFailed(Exception ex, string id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "App '{Id}' ended on an error.")]
    private partial void LogRunFailed(Exception ex, string id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "App package '{Id}': could not create its state dir '{StateDir}'.")]
    private partial void LogStateDirFailed(Exception ex, string id, string stateDir);
}
