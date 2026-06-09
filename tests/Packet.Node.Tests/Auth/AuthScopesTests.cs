using Packet.Node.Core.Auth;

namespace Packet.Node.Tests.Auth;

/// <summary>
/// The scope implication model: admin ⊃ operate ⊃ read, and nothing satisfies an
/// unknown/absent grant.
/// </summary>
[Trait("Category", "Node")]
public class AuthScopesTests
{
    [Theory]
    // admin satisfies everything
    [InlineData(AuthScopes.Admin, AuthScopes.Read, true)]
    [InlineData(AuthScopes.Admin, AuthScopes.Operate, true)]
    [InlineData(AuthScopes.Admin, AuthScopes.Admin, true)]
    // operate satisfies operate + read, not admin
    [InlineData(AuthScopes.Operate, AuthScopes.Read, true)]
    [InlineData(AuthScopes.Operate, AuthScopes.Operate, true)]
    [InlineData(AuthScopes.Operate, AuthScopes.Admin, false)]
    // read satisfies only read
    [InlineData(AuthScopes.Read, AuthScopes.Read, true)]
    [InlineData(AuthScopes.Read, AuthScopes.Operate, false)]
    [InlineData(AuthScopes.Read, AuthScopes.Admin, false)]
    // an unknown / absent grant satisfies nothing
    [InlineData(null, AuthScopes.Read, false)]
    [InlineData("bogus", AuthScopes.Read, false)]
    public void Satisfies_encodes_the_implication(string? granted, string required, bool expected)
    {
        AuthScopes.Satisfies(granted, required).Should().Be(expected);
    }

    [Fact]
    public void IsKnown_accepts_only_the_three_scopes()
    {
        AuthScopes.IsKnown(AuthScopes.Read).Should().BeTrue();
        AuthScopes.IsKnown(AuthScopes.Operate).Should().BeTrue();
        AuthScopes.IsKnown(AuthScopes.Admin).Should().BeTrue();
        AuthScopes.IsKnown("superuser").Should().BeFalse();
        AuthScopes.IsKnown(null).Should().BeFalse();
    }
}
