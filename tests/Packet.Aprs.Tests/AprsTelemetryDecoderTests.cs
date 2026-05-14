using Packet.Aprs;

namespace Packet.Aprs.Tests;

public class AprsTelemetryDecoderTests
{
    [Fact]
    public void Decodes_Spec_Example()
    {
        // APRS101 §13 example.
        var info = System.Text.Encoding.ASCII.GetBytes("T#005,199,000,255,073,123,01101001");
        AprsTelemetryDecoder.TryDecode(info, out var t).Should().BeTrue();
        t.Sequence.Should().Be("005");
        t.AnalogValues.Should().Equal(199.0, 0.0, 255.0, 73.0, 123.0);
        t.DigitalBits.Should().Equal(false, true, true, false, true, false, false, true);
        t.Comment.Should().BeEmpty();
    }

    [Fact]
    public void Decodes_MIC_Sequence_Without_Comma()
    {
        // §13 says the comma after MIC is optional.
        var info = System.Text.Encoding.ASCII.GetBytes("T#MIC199,000,255,073,123,01101001");
        AprsTelemetryDecoder.TryDecode(info, out var t).Should().BeTrue();
        t.Sequence.Should().Be("MIC");
        t.AnalogValues.Should().Equal(199.0, 0.0, 255.0, 73.0, 123.0);
    }

    [Theory]
    // Real corpus samples.
    [InlineData("T#026,0,0,0,42,1,00000000",                              "026", 0.0, 42.0, 1.0)]
    [InlineData("T#398,000,000,000,000,000,00000000",                     "398", 0.0,  0.0, 0.0)]
    [InlineData("T#127,184,250,150,099,015,00001111",                     "127", 184.0, 99.0, 15.0)]
    [InlineData("T#949,3.2,0.0,16.0,0.0,0.0,00000000",                    "949", 3.2,   0.0, 0.0)]
    public void Decodes_Real_Corpus_Telemetry(
        string infoText, string expectedSequence,
        double expectedFirstAnalog, double expectedFourthAnalog, double expectedFifthAnalog)
    {
        var info = System.Text.Encoding.ASCII.GetBytes(infoText);
        AprsTelemetryDecoder.TryDecode(info, out var t).Should().BeTrue();
        t.Sequence.Should().Be(expectedSequence);
        t.AnalogValues[0].Should().BeApproximately(expectedFirstAnalog, 0.01);
        t.AnalogValues[3].Should().BeApproximately(expectedFourthAnalog, 0.01);
        t.AnalogValues[4].Should().BeApproximately(expectedFifthAnalog, 0.01);
        t.DigitalBits.Count.Should().Be(8);
    }

    [Fact]
    public void Captures_Trailing_Comment()
    {
        var info = System.Text.Encoding.ASCII.GetBytes(
            "T#012,0.00,0.00,0,0,0.0,00000000,SimplexLogic");
        AprsTelemetryDecoder.TryDecode(info, out var t).Should().BeTrue();
        t.Sequence.Should().Be("012");
        t.Comment.Should().Be("SimplexLogic");
    }

    [Fact]
    public void Comment_Preserves_Embedded_Commas()
    {
        var info = System.Text.Encoding.ASCII.GetBytes(
            "T#999,1,2,3,4,5,11111111,a,b,c");
        AprsTelemetryDecoder.TryDecode(info, out var t).Should().BeTrue();
        t.Comment.Should().Be("a,b,c");
    }

    [Theory]
    [InlineData("T#005,199,000,255,073")]                          // too few analog values
    [InlineData("T")]                                              // no # marker
    [InlineData("T#005,abc,000,255,073,123,01101001")]             // non-numeric analog
    [InlineData("T#005,199,000,255,073,123,01234567")]             // non-binary digital
    public void Rejects_Malformed(string infoText)
    {
        var info = System.Text.Encoding.ASCII.GetBytes(infoText);
        AprsTelemetryDecoder.TryDecode(info, out _).Should().BeFalse();
    }

    [Fact]
    public void Strips_DTI_If_Present()
    {
        var with    = System.Text.Encoding.ASCII.GetBytes("T#005,1,2,3,4,5,00000000");
        var without = System.Text.Encoding.ASCII.GetBytes("#005,1,2,3,4,5,00000000");
        AprsTelemetryDecoder.TryDecode(with,    out var a).Should().BeTrue();
        AprsTelemetryDecoder.TryDecode(without, out var b).Should().BeTrue();
        a.Sequence.Should().Be(b.Sequence);
        a.AnalogValues.Should().Equal(b.AnalogValues);
    }
}
