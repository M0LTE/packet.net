using Packet.Core;

namespace Packet.Core.Tests;

public class CallsignTests
{
    [Theory]
    [InlineData("G7XYZ", 0, "G7XYZ")]
    [InlineData("G7XYZ", 7, "G7XYZ-7")]
    [InlineData("M0LTE", 1, "M0LTE-1")]
    [InlineData("K1ABC", 15, "K1ABC-15")]
    [InlineData("WB2OSZ", 0, "WB2OSZ")]
    public void Construct_And_Format(string @base, byte ssid, string expected)
    {
        var c = new Callsign(@base, ssid);
        c.ToString().Should().Be(expected);
        c.Base.Should().Be(@base);
        c.Ssid.Should().Be(ssid);
    }

    [Theory]
    [InlineData("G7XYZ", "G7XYZ", 0)]
    [InlineData("G7XYZ-0", "G7XYZ", 0)]
    [InlineData("G7XYZ-7", "G7XYZ", 7)]
    [InlineData("M0LTE-15", "M0LTE", 15)]
    public void Parse_RoundTrips(string text, string expectedBase, byte expectedSsid)
    {
        var c = Callsign.Parse(text);
        c.Base.Should().Be(expectedBase);
        c.Ssid.Should().Be(expectedSsid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("g7xyz")]            // lowercase
    [InlineData("G7XYZ-16")]         // SSID > 15
    [InlineData("G7XYZ-A")]          // non-numeric SSID
    [InlineData("TOOLONGCALL")]      // > 6 chars
    [InlineData("G7-XY")]            // dash mid-base
    [InlineData("G7!XY")]            // non-alphanumeric
    public void TryParse_Rejects_Invalid(string? text)
    {
        Callsign.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    public void Constructor_Rejects_Invalid_Ssid()
    {
        ((Action)(() => new Callsign("G7XYZ", 16))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_Accepts_Empty_Base()
    {
        // AX.25 v2.2 §3.12.2 doesn't specify a minimum callsign length, and
        // §6.1.1 acknowledges non-callsign destination content exists in
        // practice. Some implementations emit UI frames (beacons, station
        // identification) with an all-space address slot, which decodes to
        // an empty Base. Permitted on the wire-parse path.
        var c = new Callsign("", 0);
        c.Base.Should().Be("");
        c.Ssid.Should().Be((byte)0);
        c.ToString().Should().Be("");
    }

    [Fact]
    public void TryParse_Still_Rejects_Empty_Text()
    {
        // Parse / TryParse remain strict — they're for user-typed text input
        // where empty is a typo, not a legitimate wire value.
        Callsign.TryParse("", out _).Should().BeFalse();
    }

    [Fact]
    public void Equality_Treats_Same_Base_And_Ssid_As_Equal()
    {
        var a = new Callsign("G7XYZ", 7);
        var b = new Callsign("G7XYZ", 7);
        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
