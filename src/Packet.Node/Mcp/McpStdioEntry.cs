using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Packet.Mcp;
using Packet.Mcp.Tools;

namespace Packet.Node.Mcp;

/// <summary>
/// The <c>pdn mcp</c> subcommand: an MCP server over <b>stdio</b> for local clients
/// (Claude Code, etc.). It bridges to the running node's loopback REST API via
/// <see cref="RestNodeMcpBackend"/> — a stdio process can't share the live node's
/// in-proc state, so it talks to <c>127.0.0.1</c>. The caller is the OS-trusted local
/// user (<see cref="McpCaller.LocalStdio"/>, all scopes). See docs/mcp-design.md.
/// </summary>
public static class McpStdioEntry
{
    /// <summary>The default node base URL (the web listener's loopback default).</summary>
    public const string DefaultNodeUrl = "http://127.0.0.1:8080";

    /// <summary>
    /// Run the stdio MCP server until stdin closes. <paramref name="args"/> is the full
    /// process argv (the first element is <c>mcp</c>); <c>--node-url &lt;url&gt;</c> or the
    /// <c>PDN_NODE_URL</c> env var override the node base URL.
    /// </summary>
    public static async Task RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var builder = Host.CreateApplicationBuilder(args);

        // stdout is the MCP protocol stream — every log MUST go to stderr.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        var baseUrl = ResolveNodeUrl(args);
        builder.Services.AddSingleton<IMcpCallerAccessor, LocalStdioCallerAccessor>();
        builder.Services.AddHttpClient<INodeMcpBackend, RestNodeMcpBackend>(
            c => c.BaseAddress = new Uri(baseUrl, UriKind.Absolute));
        builder.Services.AddTransient<ReadTools>();
        builder.Services.AddTransient<WriteTools>();

        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<DiagnosticTools>()
            .WithTools<ReadTools>()
            .WithTools<WriteTools>();

        await builder.Build().RunAsync().ConfigureAwait(false);
    }

    private static string ResolveNodeUrl(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--node-url")
            {
                return args[i + 1];
            }
        }
        var env = Environment.GetEnvironmentVariable("PDN_NODE_URL");
        return string.IsNullOrWhiteSpace(env) ? DefaultNodeUrl : env;
    }
}
