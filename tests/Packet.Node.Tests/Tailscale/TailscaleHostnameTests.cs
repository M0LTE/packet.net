using Packet.Node.Core.Tailscale;

namespace Packet.Node.Tests.Tailscale;

public class TailscaleHostnameTests
{
    [Theory]
    // An explicit hostname always wins (trimmed), regardless of callsign.
    [InlineData("rdg-pdn", "GB7RDG", "rdg-pdn")]
    [InlineData("  custom  ", "GB7RDG", "custom")]
    // Empty ⇒ derive <callsign>-pdn from the lowercased base callsign.
    [InlineData("", "GB7RDG", "gb7rdg-pdn")]
    [InlineData(null, "M9YYY", "m9yyy-pdn")]
    [InlineData("   ", "M0LTE", "m0lte-pdn")]
    // The SSID is dropped; punctuation reduced to [a-z0-9].
    [InlineData("", "M0LTE-7", "m0lte-pdn")]
    [InlineData("", "gb7rdg-10", "gb7rdg-pdn")]
    // No usable callsign ⇒ the bare "pdn" fallback.
    [InlineData("", "", "pdn")]
    [InlineData("", null, "pdn")]
    [InlineData("", "-7", "pdn")]
    public void Resolves_explicit_or_callsign_prefixed_or_fallback(string? configured, string? callsign, string expected)
    {
        TailscaleHostname.Resolve(configured, callsign).Should().Be(expected);
    }
}
