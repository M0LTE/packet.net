using Packet.Ax25;
using Packet.Core;
using Packet.Mcp.Decoding;

namespace Packet.Mcp.Tests;

/// <summary>
/// Edge paths of <see cref="FrameDecoder"/> the main suite didn't reach: the
/// explicit <see cref="FrameDecoder.Framing"/> overrides (vs. auto-detect), the
/// KISS-without-a-data-frame rejection, modulo-128 (extended) control decode, the
/// unknown-PID and empty-info renderings, and empty input.
/// </summary>
public class FrameDecoderEdgeTests
{
    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);

    private static byte[] KissWrap(byte[] ax25, byte portCommand)
    {
        var kiss = new byte[ax25.Length + 3];
        kiss[0] = 0xC0;
        kiss[1] = portCommand;   // (port << 4) | command
        ax25.CopyTo(kiss, 2);
        kiss[^1] = 0xC0;
        return kiss;
    }

    [Fact]
    public void Empty_input_is_rejected()
    {
        Action act = () => FrameDecoder.Decode("");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Explicit_Kiss_framing_unwraps_even_when_not_auto_detecting()
    {
        var ax25 = Ax25Frame.Ui(new Callsign("APRS"), new Callsign("M0LTE"), "k"u8).ToBytes();
        var kiss = KissWrap(ax25, 0x10);   // port 1, Data

        var d = FrameDecoder.Decode(Hex(kiss), FrameDecoder.Framing.Kiss);

        d.Framing.Should().Be("kiss");
        d.KissPort.Should().Be(1);
        d.Source.Should().Be("M0LTE");
    }

    [Fact]
    public void Forcing_Raw_framing_does_not_unwrap_a_KISS_frame()
    {
        // A KISS-wrapped frame begins 0xC0; forced Raw, the decoder treats those
        // bytes as the AX.25 body, which is not a valid frame → ArgumentException.
        var ax25 = Ax25Frame.Ui(new Callsign("APRS"), new Callsign("M0LTE"), "k"u8).ToBytes();
        var kiss = KissWrap(ax25, 0x00);

        Action act = () => FrameDecoder.Decode(Hex(kiss), FrameDecoder.Framing.Raw);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_KISS_frame_with_no_data_command_is_rejected()
    {
        // KISS command 0x06 (not Data=0x0) → the decoder finds no data frame to decode.
        var ax25 = Ax25Frame.Ui(new Callsign("APRS"), new Callsign("M0LTE"), "k"u8).ToBytes();
        var kiss = KissWrap(ax25, 0x06);

        Action act = () => FrameDecoder.Decode(Hex(kiss));   // auto-detects KISS (leading FEND)
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Extended_decodes_an_I_frame_as_modulo_128()
    {
        // Address (14) + 2-octet control (mod-128 I, N(S)=3, N(R)=5) + pid + info.
        // I control octet 1: N(S) in bits 7..1, bit0 = 0 (I). octet 2: N(R) in bits 7..1.
        var addr = Ax25Frame.Ui(new Callsign("GB7RDG"), new Callsign("M0LTE"), ReadOnlySpan<byte>.Empty)
            .ToBytes()[..14];
        var bytes = new byte[14 + 2 + 1 + 2];
        addr.CopyTo(bytes, 0);
        bytes[14] = (byte)(3 << 1);   // N(S) = 3, I-frame (bit0 = 0)
        bytes[15] = (byte)(5 << 1);   // N(R) = 5
        bytes[16] = 0xF0;             // PID
        bytes[17] = (byte)'h';
        bytes[18] = (byte)'i';

        var d = FrameDecoder.Decode(Hex(bytes), FrameDecoder.Framing.Raw, extended: true);

        d.Modulo.Should().Be(128);
        d.FrameClass.Should().Be("I");
        d.Ns.Should().Be(3);
        d.Nr.Should().Be(5);
        d.InfoText.Should().Be("hi");
    }

    [Fact]
    public void An_unknown_PID_has_a_null_name_but_the_raw_byte_is_reported()
    {
        var frame = Ax25Frame.Ui(new Callsign("APRS"), new Callsign("M0LTE"), "x"u8, pid: 0x99);

        var d = FrameDecoder.Decode(Hex(frame.ToBytes()));

        d.Pid.Should().Be(0x99);
        d.PidName.Should().BeNull();
    }

    [Fact]
    public void Empty_info_renders_as_an_empty_string()
    {
        var frame = Ax25Frame.Ui(new Callsign("APRS"), new Callsign("M0LTE"), ReadOnlySpan<byte>.Empty);

        var d = FrameDecoder.Decode(Hex(frame.ToBytes()));

        d.InfoLength.Should().Be(0);
        d.InfoText.Should().BeEmpty();
        d.InfoHex.Should().BeEmpty();
    }
}
