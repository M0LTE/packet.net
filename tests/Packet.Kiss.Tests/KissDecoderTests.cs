using Packet.Kiss;

namespace Packet.Kiss.Tests;

public class KissDecoderTests
{
    [Fact]
    public void Decodes_Empty_Data_Frame()
    {
        var d = new KissDecoder();
        var frames = d.Push(new byte[] { 0xC0, 0x00, 0xC0 });
        frames.Count.Should().Be(1);
        frames[0].Port.Should().Be((byte)0);
        frames[0].Command.Should().Be(KissCommand.Data);
        frames[0].Payload.Should().BeEmpty();
    }

    [Fact]
    public void Decodes_Frame_With_Payload()
    {
        var d = new KissDecoder();
        var frames = d.Push(new byte[] { 0xC0, 0x10, 0x01, 0x02, 0x03, 0xC0 });
        frames.Count.Should().Be(1);
        frames[0].Port.Should().Be((byte)1);
        frames[0].Command.Should().Be(KissCommand.Data);
        frames[0].Payload.Should().Equal(new byte[] { 0x01, 0x02, 0x03 });
    }

    [Fact]
    public void Decodes_Escaped_FEND()
    {
        var d = new KissDecoder();
        var frames = d.Push(new byte[] { 0xC0, 0x00, 0xDB, 0xDC, 0xC0 });
        frames.Count.Should().Be(1);
        frames[0].Payload.Should().Equal(new byte[] { 0xC0 });
    }

    [Fact]
    public void Decodes_Escaped_FESC()
    {
        var d = new KissDecoder();
        var frames = d.Push(new byte[] { 0xC0, 0x00, 0xDB, 0xDD, 0xC0 });
        frames.Count.Should().Be(1);
        frames[0].Payload.Should().Equal(new byte[] { 0xDB });
    }

    [Fact]
    public void Drops_Empty_Inter_Frame_FENDs()
    {
        var d = new KissDecoder();
        var frames = d.Push(new byte[] { 0xC0, 0xC0, 0xC0, 0x00, 0xAA, 0xC0, 0xC0 });
        frames.Count.Should().Be(1);
        frames[0].Payload.Should().Equal(new byte[] { 0xAA });
    }

    [Fact]
    public void Reassembles_Across_Chunks()
    {
        var d = new KissDecoder();
        var f1 = d.Push(new byte[] { 0xC0, 0x00, 0x01 });
        var f2 = d.Push(new byte[] { 0x02, 0xDB });
        var f3 = d.Push(new byte[] { 0xDC, 0x03, 0xC0 });

        f1.Count.Should().Be(0);
        f2.Count.Should().Be(0);
        f3.Count.Should().Be(1);
        f3[0].Payload.Should().Equal(new byte[] { 0x01, 0x02, 0xC0, 0x03 });
    }

    [Fact]
    public void Decodes_Multiple_Frames_In_One_Push()
    {
        var d = new KissDecoder();
        var frames = d.Push(new byte[] { 0xC0, 0x00, 0xAA, 0xC0, 0xC0, 0x10, 0xBB, 0xC0 });
        frames.Count.Should().Be(2);
        frames[0].Payload.Should().Equal(new byte[] { 0xAA });
        frames[1].Port.Should().Be((byte)1);
        frames[1].Payload.Should().Equal(new byte[] { 0xBB });
    }

    [Fact]
    public void Reset_Discards_Partial_Frame()
    {
        var d = new KissDecoder();
        d.Push(new byte[] { 0xC0, 0x00, 0xAA });
        d.Reset();
        var frames = d.Push(new byte[] { 0xC0, 0x10, 0xBB, 0xC0 });
        frames.Count.Should().Be(1);
        frames[0].Port.Should().Be((byte)1);
        frames[0].Payload.Should().Equal(new byte[] { 0xBB });
    }
}
