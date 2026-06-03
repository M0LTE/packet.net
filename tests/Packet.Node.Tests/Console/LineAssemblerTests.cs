using System.Linq;
using System.Text;
using Packet.Node.Core.Console;

namespace Packet.Node.Tests.Console;

/// <summary>
/// Line-editing behaviour of <see cref="LineAssembler"/> — in particular
/// backspace/DEL handling, which must stay in step with the telnet connection's
/// server-side echo so the parsed line matches what the user sees.
/// </summary>
public sealed class LineAssemblerTests
{
    [Fact]
    public void Backspace_erases_the_previous_buffered_character()
    {
        var a = new LineAssembler();
        // "AB" <BS> "C" <CR>  ->  the line is "AC"
        var line = a.Push(new byte[] { (byte)'A', (byte)'B', 0x08, (byte)'C', (byte)'\r' }).Single();
        Encoding.ASCII.GetString(line).Should().Be("AC");
    }

    [Fact]
    public void Del_erases_too_and_backspace_on_an_empty_line_is_a_noop()
    {
        var a = new LineAssembler();
        // <BS>(noop) 'X' <DEL>(erase X) 'Y' <LF>  ->  "Y"
        var line = a.Push(new byte[] { 0x08, (byte)'X', 0x7f, (byte)'Y', (byte)'\n' }).Single();
        Encoding.ASCII.GetString(line).Should().Be("Y");
    }

    [Fact]
    public void Backspace_does_not_cross_a_line_boundary()
    {
        var a = new LineAssembler();
        // "A" <CR> then <BS> 'B' <CR>: the BS after the terminator has nothing to
        // erase, so the second line is just "B".
        var lines = a.Push(new byte[] { (byte)'A', (byte)'\r', 0x08, (byte)'B', (byte)'\r' }).ToList();
        lines.Should().HaveCount(2);
        Encoding.ASCII.GetString(lines[0]).Should().Be("A");
        Encoding.ASCII.GetString(lines[1]).Should().Be("B");
    }
}
