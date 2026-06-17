using Microsoft.Extensions.Logging.Abstractions;
using Packet.Node.Core.Applications;
using Packet.Node.Core.Configuration;
using Packet.Node.Core.Console;
using Packet.Node.Tests.Support;

namespace Packet.Node.Tests.Applications;

/// <summary>
/// The <c>pdn-app/1</c> stdio bridge (<see cref="ExternalProcessApplication"/>). Uses
/// <c>/bin/cat</c> as a stand-in app: cat echoes its stdin to stdout, so whatever the bridge
/// writes to the child (the connect header, then each assembled user line) comes straight back
/// through the forward pump — letting us assert the header format, the newline translation, and
/// the teardown without a bespoke fixture. Linux-only (CI is Linux; cat is the echo oracle).
/// </summary>
[Trait("Category", "Node")]
public sealed class ExternalProcessApplicationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static ExternalProcessApplication CatApp() => new(
        new ApplicationConfig { Id = "echo", Command = "ECHO", Executable = "/bin/cat" },
        NullLogger.Instance);

    private static NodeAppContext Ctx(NodeTransportKind kind = NodeTransportKind.Ax25, params string[] args) => new()
    {
        Callsign = "M0LTE-7",
        Transport = kind,
        PortId = "gb7rdg",
        Args = args,
    };

    [Fact]
    public async Task Writes_the_connect_header_then_echoes_user_lines_with_transport_newline()
    {
        if (!OperatingSystem.IsLinux()) return;   // Linux-only: relies on /bin/cat

        var conn = new DriveableConnection("M0LTE-7", NodeTransportKind.Ax25);
        var app = CatApp();
        var run = app.RunAsync(conn, Ctx(NodeTransportKind.Ax25, "last", "5"));

        // cat echoes the header the bridge wrote to its stdin → forwarded back to us.
        await Wait.ForAsync(() => conn.Output.Contains("pdn-app: 1"), "header echoed back");
        await Wait.ForAsync(() => conn.Output.Contains("args: last 5"), "args header line present");

        // A user line is assembled, fed to the app as \n-terminated, echoed back, and forwarded
        // to us with the AX.25 newline (a bare CR — never the \n the app emitted).
        conn.Inject("hello world\r");
        await Wait.ForAsync(() => conn.Output.Contains("hello world"), "user line echoed back");

        Assert.Contains("callsign: M0LTE-7", conn.Output, StringComparison.Ordinal);
        Assert.Contains("transport: ax25", conn.Output, StringComparison.Ordinal);
        Assert.Contains("port: gb7rdg", conn.Output, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', conn.Output);   // every \n translated to CR for AX.25
        Assert.Contains('\r', conn.Output);

        conn.Drop();                                 // user disconnects → bridge closes cat's stdin → cat exits
        await run.WaitAsync(Timeout);                // RunAsync returns (no hang, no kill needed)
    }

    [Fact]
    public async Task Telnet_gets_crlf_newlines()
    {
        if (!OperatingSystem.IsLinux()) return;

        var conn = new DriveableConnection("127.0.0.1:5000", NodeTransportKind.Telnet);
        var app = CatApp();
        var run = app.RunAsync(conn, Ctx(NodeTransportKind.Telnet));

        await Wait.ForAsync(() => conn.Output.Contains("transport: telnet"), "header for telnet");
        Assert.Contains("\r\n", conn.Output);        // CR-LF for telnet

        conn.Drop();
        await run.WaitAsync(Timeout);
    }

    [Fact]
    public async Task An_app_that_exits_on_its_own_returns_control_to_the_console()
    {
        if (!OperatingSystem.IsLinux()) return;

        // /bin/echo writes a line then exits immediately — stdout EOF — even though the session
        // stays open. RunAsync must return (the user is dropped back to the node prompt).
        var conn = new DriveableConnection("M0LTE-7", NodeTransportKind.Ax25);
        var app = new ExternalProcessApplication(
            new ApplicationConfig { Id = "hi", Command = "HI", Executable = "/bin/echo", Args = ["hi there"] },
            NullLogger.Instance);

        await app.RunAsync(conn, Ctx()).WaitAsync(Timeout);   // returns without us dropping the session

        Assert.Contains("hi there", conn.Output, StringComparison.Ordinal);
        Assert.False(conn.Completion.IsCompleted);            // we never dropped the user
    }

    [Fact]
    public async Task A_missing_command_throws_ApplicationStartException()
    {
        var conn = new DriveableConnection("M0LTE-7", NodeTransportKind.Ax25);
        var app = new ExternalProcessApplication(
            new ApplicationConfig { Id = "ghost", Command = "GHOST", Executable = "/no/such/binary-xyzzy" },
            NullLogger.Instance);

        await Assert.ThrowsAsync<ApplicationStartException>(() => app.RunAsync(conn, Ctx()));
    }

    [Fact]
    public void Constructing_without_a_command_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ExternalProcessApplication(new ApplicationConfig { Id = "x", Command = "X", Executable = null }));
    }
}
