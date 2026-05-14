using Packet.Aprs;

namespace Packet.Aprs.Tests;

public class AprsCallsignCoercionTests
{
    [Fact]
    public void Coerce_Returns_Strict_Unchanged_When_Already_Valid()
    {
        AprsCallsign.TryParse("M0LTE-7", out var c).Should().BeTrue();
        var strict = c.ToStrictCallsignOrCoerced();
        strict.Base.Should().Be("M0LTE");
        strict.Ssid.Should().Be((byte)7);
    }

    [Fact]
    public void Coerce_Letter_Ssid_Becomes_Numeric_Fallback()
    {
        AprsCallsign.TryParse("K0MVH-D", out var c).Should().BeTrue();
        var strict = c.ToStrictCallsignOrCoerced();
        strict.Base.Should().Be("K0MVH");
        strict.Ssid.Should().Be((byte)1, "default fallbackSsid is 1");
    }

    [Fact]
    public void Coerce_Letter_Ssid_Uses_Custom_Fallback()
    {
        AprsCallsign.TryParse("K0MVH-D", out var c).Should().BeTrue();
        var strict = c.ToStrictCallsignOrCoerced(fallbackSsid: 7);
        strict.Ssid.Should().Be((byte)7);
    }

    [Fact]
    public void Coerce_Lowercase_Base_Becomes_Uppercase()
    {
        AprsCallsign.TryParse("aprsdroid", out var c).Should().BeTrue();
        var strict = c.ToStrictCallsignOrCoerced();
        strict.Base.Should().Be("APRSDR", "uppercase + truncated to 6 chars");
        strict.Ssid.Should().Be((byte)0);
    }

    [Fact]
    public void Coerce_Long_Base_Truncates_To_Six()
    {
        AprsCallsign.TryParse("LONGCALLG", out var c).Should().BeTrue();
        var strict = c.ToStrictCallsignOrCoerced();
        strict.Base.Should().Be("LONGCA");
    }

    [Fact]
    public void Coerce_Mixed_Case_Letter_Ssid_Combo()
    {
        AprsCallsign.TryParse("zs6atz-D", out var c).Should().BeTrue();
        var strict = c.ToStrictCallsignOrCoerced();
        strict.Base.Should().Be("ZS6ATZ");
        strict.Ssid.Should().Be((byte)1);
    }
}
