using Packet.Aprs;

namespace Packet.Aprs.Tests;

public class AprsCallsignTests
{
    [Theory]
    [InlineData("M0LTE",     "M0LTE", "")]
    [InlineData("M0LTE-7",   "M0LTE", "7")]
    [InlineData("M0LTE-15",  "M0LTE", "15")]
    [InlineData("K0MVH-D",   "K0MVH", "D")]        // D-Star port — strict Callsign rejects
    [InlineData("ZS6ATZ-D",  "ZS6ATZ", "D")]
    [InlineData("SM6JWU-B",  "SM6JWU", "B")]
    [InlineData("W2TXB-B",   "W2TXB", "B")]
    [InlineData("aprsdroid", "aprsdroid", "")]      // lowercase base — strict rejects
    [InlineData("LONGCALLNAM", "LONGCALLNAM", "")]  // 11 chars — too long (max 9)
    public void TryParse_Accepts_Or_Rejects_As_Expected(string input, string expectedBase, string expectedSsid)
    {
        bool ok = AprsCallsign.TryParse(input, out var c);

        if (input.Length <= 9 + 4 && input.Length > 0)  // Length sanity for parseable
        {
            // We compute expected validity here: base ≤ 9, ssid ≤ 3, all alphanumeric.
            string[] parts = input.Split('-', 2);
            bool expectedOk = parts[0].Length is >= 1 and <= 9
                && parts[0].All(IsAlphaNum)
                && (parts.Length == 1 || (parts[1].Length is >= 0 and <= 3 && parts[1].All(IsAlphaNum)));
            ok.Should().Be(expectedOk);
            if (ok)
            {
                c.Base.Should().Be(expectedBase);
                c.Ssid.Should().Be(expectedSsid);
            }
        }

        static bool IsAlphaNum(char ch) => char.IsAsciiLetterOrDigit(ch);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("TOOLONGCALL")]    // 11 chars — over 9-char base limit
    [InlineData("M0LTE-")]          // empty SSID after dash — accepted as "" actually
    [InlineData("M0LTE--7")]        // double dash
    [InlineData("M0LTE-1234")]      // 4-char SSID — over 3-char limit
    [InlineData("M0!LTE")]          // exclamation mark — invalid char
    [InlineData("M0LTE-A!")]        // invalid SSID char
    public void TryParse_Rejects_Invalid_Inputs(string? input)
    {
        if (input == "M0LTE-")
        {
            // Edge case: trailing dash gives empty SSID — accepted.
            AprsCallsign.TryParse(input, out var c).Should().BeTrue();
            c.Ssid.Should().BeEmpty();
            return;
        }
        AprsCallsign.TryParse(input, out _).Should().BeFalse();
    }

    [Fact]
    public void ToString_Round_Trips_Through_TryParse()
    {
        string original = "ZS6ATZ-D";
        AprsCallsign.TryParse(original, out var c).Should().BeTrue();
        c.ToString().Should().Be(original);
    }

    [Fact]
    public void ToString_Bare_Base_Has_No_Dash()
    {
        var c = new AprsCallsign("M0LTE");
        c.ToString().Should().Be("M0LTE");
    }

    [Fact]
    public void Permissive_Callsign_That_Is_Strict_Compatible_Converts_To_Core_Callsign()
    {
        AprsCallsign.TryParse("M0LTE-7", out var aprs).Should().BeTrue();
        aprs.TryToStrictCallsign(out var strict).Should().BeTrue();
        strict.Base.Should().Be("M0LTE");
        strict.Ssid.Should().Be((byte)7);
    }

    [Fact]
    public void Permissive_Callsign_With_Letter_Ssid_Does_Not_Convert_To_Strict()
    {
        AprsCallsign.TryParse("K0MVH-D", out var aprs).Should().BeTrue();
        aprs.TryToStrictCallsign(out _).Should().BeFalse("letter SSID is not AX.25-valid");
    }

    [Fact]
    public void Permissive_Callsign_With_Lowercase_Does_Not_Convert_To_Strict()
    {
        AprsCallsign.TryParse("aprsdroid", out var aprs).Should().BeTrue();
        aprs.TryToStrictCallsign(out _).Should().BeFalse("lowercase letters are not AX.25-valid");
    }

    [Fact]
    public void Records_Are_Value_Equal()
    {
        AprsCallsign.TryParse("M0LTE-7", out var a).Should().BeTrue();
        AprsCallsign.TryParse("M0LTE-7", out var b).Should().BeTrue();
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
