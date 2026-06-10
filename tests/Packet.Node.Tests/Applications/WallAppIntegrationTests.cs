using Microsoft.Extensions.Logging.Abstractions;
using Packet.Node.Core.Applications;
using Packet.Node.Core.Configuration;
using Packet.Node.Core.Console;
using Packet.Node.Tests.Support;

namespace Packet.Node.Tests.Applications;

/// <summary>
/// The cross-language end-to-end proof: the C# <see cref="ExternalProcessApplication"/> bridge
/// driving the <b>actual shipped</b> Python WALL app (<c>examples/wall/wall.py</c>) over the
/// <c>pdn-app/1</c> wire — the two halves of the language boundary, together. Unit tests cover
/// each half (the bridge via /bin/cat; WALL via its own python tests); this proves the seam
/// itself works between a .NET node and a non-.NET app. Linux + python3 only (CI is Linux).
/// </summary>
[Trait("Category", "Node")]
public sealed class WallAppIntegrationTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private readonly string workDir;

    public WallAppIntegrationTests()
    {
        workDir = Path.Combine(Path.GetTempPath(), "pdn-wall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);   // WALL writes wall.txt in its working directory
    }

    public void Dispose()
    {
        try { Directory.Delete(workDir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    [Fact]
    public async Task A_user_posts_and_reads_the_wall_through_the_bridge()
    {
        var python = FindPython();
        if (python is null || !OperatingSystem.IsLinux())
        {
            return;   // Linux + python3 only
        }

        var wallPy = Path.Combine(RepoRoot(), "examples", "wall", "wall.py");
        Assert.True(File.Exists(wallPy), $"WALL app not found at {wallPy}");

        var app = new ExternalProcessApplication(
            new ApplicationConfig
            {
                Id = "wall",
                Match = "WALL",
                Command = python,
                Args = [wallPy],
                WorkingDirectory = workDir,   // wall.txt lands here (default WALL_FILE)
            },
            NullLogger.Instance);

        var conn = new DriveableConnection("M0LTE-7", NodeTransportKind.Ax25);
        var ctx = new NodeAppContext { Callsign = "M0LTE-7", Transport = NodeTransportKind.Ax25, PortId = "gb7rdg" };
        var run = app.RunAsync(conn, ctx);

        // WALL's banner appears (it read our connect header + counted posts).
        await Wait.ForAsync(() => conn.Output.Contains("WALL"), "WALL banner");

        // Post a line, then read it back — attributed to the connect-header callsign.
        conn.Inject("POST hello from the bridge\r");
        await Wait.ForAsync(() => conn.Output.Contains("posted"), "post confirmed");

        conn.Inject("LIST\r");
        await Wait.ForAsync(() => conn.Output.Contains("hello from the bridge"), "post listed back");
        Assert.Contains("M0LTE-7", conn.Output, StringComparison.Ordinal);   // attributed to the caller

        // The app emits only \n; over AX.25 the bridge must have translated every one to CR.
        Assert.DoesNotContain('\n', conn.Output);

        // The user types BYE → WALL prints a goodbye and exits → the bridge returns.
        conn.Inject("BYE\r");
        await run.WaitAsync(Timeout);

        // The post persisted to the working-dir state file (visible to the next connect).
        var wallFile = Path.Combine(workDir, "wall.txt");
        Assert.True(File.Exists(wallFile));
        Assert.Contains("hello from the bridge", await File.ReadAllTextAsync(wallFile), StringComparison.Ordinal);
    }

    private static string? FindPython() =>
        new[] { "/usr/bin/python3", "/usr/local/bin/python3", "/bin/python3" }.FirstOrDefault(File.Exists);

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, "examples", "wall")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not locate the repo root (no examples/wall above the test assembly).");
    }
}
