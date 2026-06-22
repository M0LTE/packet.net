using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Packet.Node.Core.Console;

namespace Packet.Node.Tests.Console;

/// <summary>
/// <see cref="LineBufferingNodeConnection"/> turns a char-at-a-time interactive
/// stream into one-line-per-read for a packet link: each keystroke arriving on
/// its own (the telnet client is in character mode) must coalesce into a single
/// line, terminated with one CR, so a <c>Connect</c> sends one I-frame per line
/// rather than per character.
/// </summary>
public sealed class LineBufferingNodeConnectionTests
{
    private static byte[] B(string s) => Encoding.ASCII.GetBytes(s);

    private static async Task<List<string>> ReadAllLinesAsync(LineBufferingNodeConnection c)
    {
        var lines = new List<string>();
        while (true)
        {
            var chunk = await c.ReadAsync();
            if (chunk.IsEmpty)
            {
                break;
            }

            lines.Add(Encoding.ASCII.GetString(chunk.Span));
        }
        return lines;
    }

    [Fact]
    public async Task Characters_typed_one_at_a_time_emerge_as_one_line_with_a_single_CR()
    {
        var inner = new FakeInner(B("e"), B("c"), B("h"), B("o"), B("\r"));
        var c = new LineBufferingNodeConnection(inner);
        (await ReadAllLinesAsync(c)).Should().Equal("echo\r");
    }

    [Fact]
    public async Task A_CRLF_terminator_collapses_to_a_single_CR()
    {
        // Telnet clients send CR-LF on Enter; the packet peer must see one CR (#51).
        var inner = new FakeInner(B("on\r\n"));
        var c = new LineBufferingNodeConnection(inner);
        (await ReadAllLinesAsync(c)).Should().Equal("on\r");
    }

    [Fact]
    public async Task Multiple_lines_in_one_chunk_are_emitted_one_per_read()
    {
        var inner = new FakeInner(B("ab\rcd\r"));
        var c = new LineBufferingNodeConnection(inner);
        (await ReadAllLinesAsync(c)).Should().Equal("ab\r", "cd\r");
    }

    [Fact]
    public async Task Backspace_edits_the_line_before_it_is_sent()
    {
        var inner = new FakeInner(B("ab"), new byte[] { 0x08 }, B("c"), B("\r"));
        var c = new LineBufferingNodeConnection(inner);
        (await ReadAllLinesAsync(c)).Should().Equal("ac\r");
    }

    [Fact]
    public async Task An_empty_line_sends_a_lone_CR()
    {
        var inner = new FakeInner(B("\r"));
        var c = new LineBufferingNodeConnection(inner);
        (await ReadAllLinesAsync(c)).Should().Equal("\r");
    }

    [Fact]
    public async Task A_half_typed_line_at_EOF_is_dropped()
    {
        var inner = new FakeInner(B("ab"));   // no terminator, then EOF
        var c = new LineBufferingNodeConnection(inner);
        (await ReadAllLinesAsync(c)).Should().BeEmpty();
    }

    [Fact]
    public async Task Writes_and_metadata_delegate_to_the_inner_connection()
    {
        var inner = new FakeInner();
        var c = new LineBufferingNodeConnection(inner);
        c.TransportKind.Should().Be(NodeTransportKind.Telnet);
        c.PeerId.Should().Be("fake");
        await c.WriteAsync(B("hello"));
        inner.Written.Should().ContainSingle().Which.Should().Equal(B("hello"));
    }

    private sealed class FakeInner : INodeConnection
    {
        private readonly Queue<byte[]> chunks;
        private readonly TaskCompletionSource completion = new();

        public FakeInner(params byte[][] chunks) => this.chunks = new Queue<byte[]>(chunks);

        public List<byte[]> Written { get; } = new();
        public string PeerId => "fake";
        public NodeTransportKind TransportKind => NodeTransportKind.Telnet;
        public Task Completion => completion.Task;

        public ValueTask<ReadOnlyMemory<byte>> ReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(chunks.Count > 0
                ? (ReadOnlyMemory<byte>)chunks.Dequeue()
                : ReadOnlyMemory<byte>.Empty);

        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
        {
            Written.Add(bytes.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
