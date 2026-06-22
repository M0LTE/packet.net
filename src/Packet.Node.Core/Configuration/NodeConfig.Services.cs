namespace Packet.Node.Core.Configuration;

/// <summary>Operator-facing service strings, hot-swappable by reference.</summary>
public sealed record ServicesConfig
{
    /// <summary>Welcome banner shown on every new console connection (telnet or
    /// over-the-air). <c>{node}</c> and <c>{call}</c> placeholders are expanded.</summary>
    public string Banner { get; init; } = "Welcome to {node} ({call})";

    /// <summary>The command prompt emitted after the banner and after each
    /// command. <c>{call}</c> is expanded.</summary>
    public string Prompt { get; init; } = "{call}> ";
}
