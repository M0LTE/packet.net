using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using Packet.Node.Core.Auth;

namespace Packet.Node.Tests.Auth;

/// <summary>
/// JWT issue → validate: a fresh token validates and carries sub + scope; a tampered
/// token is rejected; an expired token is rejected (driven by the fake clock — no
/// wall-clock).
/// </summary>
[Trait("Category", "Node")]
public class JwtTokenServiceTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    private static JwtTokenService Make(FakeTimeProvider clock, TimeSpan? lifetime = null) =>
        new(Key, lifetime ?? TimeSpan.FromHours(1), clock);

    [Fact]
    public async Task A_valid_token_validates_and_carries_sub_and_scope()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        var svc = Make(clock);

        var (token, expiresAt) = svc.Issue("m0lte", AuthScopes.Admin);
        expiresAt.Should().Be(clock.GetUtcNow() + TimeSpan.FromHours(1));

        var principal = await svc.ValidateAsync(token);
        principal.Should().NotBeNull();
        principal!.FindFirst("sub")!.Value.Should().Be("m0lte");
        principal.FindFirst(AuthScopes.ScopeClaim)!.Value.Should().Be(AuthScopes.Admin);
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        var clock = new FakeTimeProvider();
        var svc = Make(clock);
        var (token, _) = svc.Issue("m0lte", AuthScopes.Read);

        // Flip a character in the signature segment.
        var parts = token.Split('.');
        parts[2] = parts[2].Length > 0 && parts[2][0] != 'A' ? "A" + parts[2][1..] : "B" + parts[2][1..];
        var tampered = string.Join('.', parts);

        (await svc.ValidateAsync(tampered)).Should().BeNull();
    }

    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        var svc = Make(clock, TimeSpan.FromMinutes(30));
        var (token, _) = svc.Issue("m0lte", AuthScopes.Read);

        // Still valid inside the window.
        (await svc.ValidateAsync(token)).Should().NotBeNull();

        // Advance past expiry → rejected (the LifetimeValidator reads the fake clock).
        clock.Advance(TimeSpan.FromMinutes(31));
        (await svc.ValidateAsync(token)).Should().BeNull();
    }

    [Fact]
    public async Task A_token_signed_by_a_different_key_is_rejected()
    {
        var clock = new FakeTimeProvider();
        var (token, _) = Make(clock).Issue("m0lte", AuthScopes.Read);

        var other = new JwtTokenService(RandomNumberGenerator.GetBytes(32), TimeSpan.FromHours(1), clock);
        (await other.ValidateAsync(token)).Should().BeNull();
    }
}
