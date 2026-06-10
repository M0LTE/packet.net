using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Packet.Node.Core.Configuration;
using Packet.Node.Core.Console;

namespace Packet.Node.Core.Applications;

/// <summary>
/// The <c>pdn-app/1</c> stdio "floor": runs a registered <see cref="ApplicationConfig"/> as an
/// out-of-process child spawned per connect, bridging the connected user's
/// <see cref="INodeConnection"/> to the child's stdin/stdout as line-oriented UTF-8 text.
/// The node and the app share <b>no code</b> — only this documented wire
/// (<c>docs/app-local-session-wire.md</c>), which is the entire separation boundary.
/// </summary>
/// <remarks>
/// <para>
/// The bridge writes a connect header (callsign / transport / port / args, each value
/// newline-sanitised so a hostile arg can't inject a header line), then runs two pumps:
/// inbound user lines → child stdin (terminator-normalised to a single <c>\n</c> via
/// <see cref="LineAssembler"/>), and child stdout → the user (every <c>\n</c>/<c>\r</c>/<c>\r\n</c>
/// translated to the transport's newline). stderr is drained to the node log.
/// </para>
/// <para>
/// The exchange ends when the child closes stdout (it exited) or the user drops (the stdout
/// pump also races <see cref="INodeConnection.Completion"/>). Teardown is unconditional: the
/// child's stdin is closed (EOF — a well-behaved app exits), and after a short grace the
/// process tree is killed. The node never crashes because an app misbehaves; a spawn failure
/// surfaces as <see cref="ApplicationStartException"/> for the host to report.
/// </para>
/// </remarks>
public sealed partial class ExternalProcessApplication : INodeApplication
{
    /// <summary>How long to wait for the child to exit on stdin-EOF before killing its tree.</summary>
    private static readonly TimeSpan TeardownGrace = TimeSpan.FromSeconds(3);

    // The newline the wire feeds the app on stdin — always a single LF, regardless of the
    // transport the user arrived on (the app never sees CR / CR-LF).
    private static readonly byte[] AppNewline = [(byte)'\n'];

    private readonly ApplicationConfig config;
    private readonly ILogger logger;

    public ExternalProcessApplication(ApplicationConfig config, ILogger? logger = null)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.logger = logger ?? NullLogger.Instance;
        if (string.IsNullOrWhiteSpace(config.Command))
        {
            throw new ArgumentException("A process application requires a command.", nameof(config));
        }
    }

    /// <inheritdoc/>
    public async Task RunAsync(INodeConnection session, NodeAppContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);

        var process = StartProcess();   // throws ApplicationStartException if the spawn fails
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var ct = linked.Token;

            try
            {
                await WriteHeaderAsync(process, context, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The child died before it even read the header — treat as a start failure.
                throw new ApplicationStartException(config.Id, config.Command!, ex);
            }

            var stderr = DrainStderrAsync(process, ct);
            var feeder = FeedSessionToProcessAsync(session, process, ct);
            var forwarder = ForwardProcessToSessionAsync(process, session, ct);

            // The exchange is over when the child stops producing output (it exited / closed
            // stdout) OR the user is gone (the forwarder also races session.Completion). Then
            // cancel the feeder + stderr drain; the finally tears the process down.
            await forwarder.ConfigureAwait(false);
            await linked.CancelAsync().ConfigureAwait(false);
            await SwallowAsync(feeder).ConfigureAwait(false);
            await SwallowAsync(stderr).ConfigureAwait(false);
        }
        finally
        {
            await TeardownAsync(process).ConfigureAwait(false);
        }
    }

    private Process StartProcess()
    {
        var psi = new ProcessStartInfo
        {
            FileName = config.Command!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,            // no shell — args are passed verbatim, no injection
            CreateNoWindow = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
        };
        foreach (var arg in config.Args)
        {
            psi.ArgumentList.Add(arg);
        }
        if (!string.IsNullOrEmpty(config.WorkingDirectory))
        {
            psi.WorkingDirectory = config.WorkingDirectory;
        }

        try
        {
            var process = Process.Start(psi)
                ?? throw new ApplicationStartException(config.Id, config.Command!, reason: "Process.Start returned null.");
            LogStarted(config.Id, config.Command!, process.Id);
            return process;
        }
        catch (ApplicationStartException)
        {
            throw;
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or DirectoryNotFoundException or InvalidOperationException)
        {
            LogStartFailed(ex, config.Id, config.Command!);
            throw new ApplicationStartException(config.Id, config.Command!, ex);
        }
    }

    // Write the pdn-app/1 connect header to the child's stdin: a key:value block terminated by
    // a blank line. Every value is newline-sanitised so a value (notably user-supplied args)
    // can't smuggle in extra header lines.
    private async Task WriteHeaderAsync(Process process, NodeAppContext ctx, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("pdn-app: 1\n");
        sb.Append("id: ").Append(SanitiseValue(config.Id)).Append('\n');
        sb.Append("callsign: ").Append(SanitiseValue(ctx.Callsign)).Append('\n');
        sb.Append("transport: ").Append(TransportToken(ctx.Transport)).Append('\n');
        sb.Append("port: ").Append(string.IsNullOrEmpty(ctx.PortId) ? "-" : SanitiseValue(ctx.PortId)).Append('\n');
        sb.Append("sysop: ").Append(ctx.SysopElevated ? '1' : '0').Append('\n');
        sb.Append("args: ").Append(SanitiseValue(string.Join(' ', ctx.Args))).Append('\n');
        sb.Append('\n');   // blank line ends the header

        var stdin = process.StandardInput.BaseStream;
        await stdin.WriteAsync(Encoding.UTF8.GetBytes(sb.ToString()), ct).ConfigureAwait(false);
        await stdin.FlushAsync(ct).ConfigureAwait(false);
    }

    // User → child stdin: each completed inbound line (terminator stripped by the assembler)
    // is written followed by a single LF, so the app reads clean \n-terminated lines whatever
    // the transport. On session EOF / drop / cancel, the child's stdin is closed (the app sees
    // EOF and is expected to exit).
    private static async Task FeedSessionToProcessAsync(INodeConnection session, Process process, CancellationToken ct)
    {
        var stdin = process.StandardInput.BaseStream;
        var assembler = new LineAssembler();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var readTask = session.ReadAsync(ct).AsTask();
                var done = await Task.WhenAny(readTask, session.Completion).ConfigureAwait(false);

                ReadOnlyMemory<byte> chunk;
                if (done != readTask)
                {
                    if (readTask.IsCompletedSuccessfully)
                    {
                        chunk = readTask.Result;   // a read also landed — deliver it, then the next loop sees the drop
                    }
                    else
                    {
                        break;   // peer gone before a read returned
                    }
                }
                else
                {
                    chunk = await readTask.ConfigureAwait(false);
                }

                if (chunk.IsEmpty)
                {
                    break;   // EOF
                }

                foreach (var line in assembler.Push(chunk))
                {
                    await stdin.WriteAsync(line, ct).ConfigureAwait(false);
                    await stdin.WriteAsync(AppNewline, ct).ConfigureAwait(false);
                }
                await stdin.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // teardown — normal.
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The child closed its stdin / exited — nothing more to feed.
        }
        finally
        {
            try { process.StandardInput.Close(); } catch { /* already gone */ }
        }
    }

    // Child stdout → user: translate every \n / \r / \r-\n the app emits to the transport's
    // newline (CR for AX.25 / NET-ROM, CR-LF for telnet) so the app only ever writes \n. The
    // \r-state carries across read chunks so a CR-LF split on a buffer boundary stays one line.
    private static async Task ForwardProcessToSessionAsync(Process process, INodeConnection session, CancellationToken ct)
    {
        var newline = session.TransportKind == NodeTransportKind.Telnet ? "\r\n" : "\r";
        var stdout = process.StandardOutput;
        var buffer = new char[2048];
        bool pendingCr = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var readTask = stdout.ReadAsync(buffer.AsMemory(), ct).AsTask();
                var done = await Task.WhenAny(readTask, session.Completion).ConfigureAwait(false);
                if (done != readTask)
                {
                    break;   // user gone — stop forwarding
                }

                int n = await readTask.ConfigureAwait(false);
                if (n == 0)
                {
                    break;   // child closed stdout (exited)
                }

                var sb = new StringBuilder(n + 16);
                for (int i = 0; i < n; i++)
                {
                    char c = buffer[i];
                    if (c == '\r')
                    {
                        sb.Append(newline);
                        pendingCr = true;
                    }
                    else if (c == '\n')
                    {
                        if (!pendingCr)
                        {
                            sb.Append(newline);
                        }
                        pendingCr = false;
                    }
                    else
                    {
                        sb.Append(c);
                        pendingCr = false;
                    }
                }

                if (sb.Length > 0)
                {
                    await session.WriteAsync(Encoding.UTF8.GetBytes(sb.ToString()), ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // teardown — normal.
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The session went away mid-write — done.
        }
    }

    private async Task DrainStderrAsync(Process process, CancellationToken ct)
    {
        try
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                if (line.Length > 0)
                {
                    LogAppStderr(config.Id, line);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // teardown — normal.
        }
        catch (Exception)
        {
            // best-effort diagnostics only.
        }
    }

    // Unconditional teardown: close stdin (EOF), give the child a short grace to exit on its
    // own, then kill the whole tree. Always disposes the Process.
    private async Task TeardownAsync(Process process)
    {
        try { process.StandardInput.Close(); } catch { /* may already be closed */ }

        try
        {
            if (!process.HasExited)
            {
                using var grace = new CancellationTokenSource(TeardownGrace);
                try
                {
                    await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Didn't exit on stdin-EOF within the grace — kill the tree.
                    try { process.Kill(entireProcessTree: true); } catch { /* race: already gone */ }
                    try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    LogKilled(config.Id);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException)
        {
            // No process associated (already torn down) — nothing to do.
        }
        finally
        {
            try { LogExited(config.Id, process.HasExited ? process.ExitCode : -1); } catch { }
            process.Dispose();
        }
    }

    private static async Task SwallowAsync(Task task)
    {
        try { await task.ConfigureAwait(false); } catch { /* pump faults are handled in-pump; ignore here */ }
    }

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static string TransportToken(NodeTransportKind kind) => kind switch
    {
        NodeTransportKind.Ax25 => "ax25",
        NodeTransportKind.NetRom => "netrom",
        NodeTransportKind.Telnet => "telnet",
        _ => "unknown",
    };

    // Strip control characters (notably CR/LF) from a header value so it stays on one line and
    // can't inject extra header keys. Collapses each stripped char to nothing.
    private static string SanitiseValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!char.IsControl(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "App '{Id}' started: {Command} (pid {Pid}).")]
    private partial void LogStarted(string id, string command, int pid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "App '{Id}' failed to start: {Command}.")]
    private partial void LogStartFailed(Exception ex, string id, string command);

    [LoggerMessage(Level = LogLevel.Information, Message = "App '{Id}' exited (code {ExitCode}).")]
    private partial void LogExited(string id, int exitCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "App '{Id}' did not exit on stdin-EOF; killed its process tree.")]
    private partial void LogKilled(string id);

    [LoggerMessage(Level = LogLevel.Debug, Message = "App '{Id}' stderr: {Line}")]
    private partial void LogAppStderr(string id, string line);
}

/// <summary>
/// Thrown when a registered application cannot be spawned (bad command, missing interpreter,
/// permissions). The host catches it, logs, and tells the user the application is unavailable —
/// the node itself never fails because an app is misconfigured.
/// </summary>
public sealed class ApplicationStartException : Exception
{
    public ApplicationStartException(string appId, string command, Exception inner)
        : base($"Application '{appId}' could not start ('{command}').", inner)
    {
        AppId = appId;
        Command = command;
    }

    public ApplicationStartException(string appId, string command, string reason)
        : base($"Application '{appId}' could not start ('{command}'): {reason}")
    {
        AppId = appId;
        Command = command;
    }

    public string AppId { get; }
    public string Command { get; }
}
