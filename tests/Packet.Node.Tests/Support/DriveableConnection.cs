using System.Text;
using System.Threading.Channels;
using Packet.Node.Core.Console;

namespace Packet.Node.Tests.Support;

/// <summary>
/// An interactively-drivable <see cref="INodeConnection"/> for application / bridge tests:
/// <see cref="Inject"/> pushes bytes "from the user" (read by the component under test),
/// <see cref="Output"/> captures everything written back to the user, and <see cref="Drop"/>
/// simulates the peer disconnecting (read EOF + <see cref="Completion"/> fires). Unlike the
/// scripted console doubles, reads block until data is injected, so it models a live session.
/// </summary>
public sealed class DriveableConnection(string peerId, NodeTransportKind kind) : INodeConnection
{
    private readonly Channel<ReadOnlyMemory<byte>> inbound =
        Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly StringBuilder output = new();
    private readonly object gate = new();
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string PeerId => peerId;
    public NodeTransportKind TransportKind => kind;
    public Task Completion => completion.Task;

    /// <summary>Everything written back to the user so far, decoded as UTF-8.</summary>
    public string Output { get { lock (gate) return output.ToString(); } }

    /// <summary>Push bytes as if the user typed them (callers usually include the line CR).</summary>
    public void Inject(string text) => inbound.Writer.TryWrite(Encoding.UTF8.GetBytes(text));

    /// <summary>Simulate the peer going away: read EOF + <see cref="Completion"/> fires.</summary>
    public void Drop()
    {
        inbound.Writer.TryComplete();
        completion.TrySetResult();
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (await inbound.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)
                && inbound.Reader.TryRead(out var chunk))
            {
                return chunk;
            }
        }
        catch (OperationCanceledException)
        {
            // fall through to EOF
        }
        return ReadOnlyMemory<byte>.Empty;   // channel completed (Drop) or cancelled → EOF
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        lock (gate) output.Append(Encoding.UTF8.GetString(bytes.Span));
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Drop();
        return ValueTask.CompletedTask;
    }
}
