using Packet.Node.Core.Auth;

namespace Packet.Node.Tests.Auth;

/// <summary>
/// Argon2id hash/verify round-trip: a correct password verifies, a wrong one does
/// not, and two hashes of the same password differ (per-user salt). The decode path
/// is exercised implicitly by every Verify (it reads the params back out of the
/// stored string).
/// </summary>
[Trait("Category", "Node")]
public class PasswordHasherTests
{
    [Fact]
    public void Correct_password_verifies()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple");
        PasswordHasher.Verify("correct horse battery staple", hash).Should().BeTrue();
    }

    [Fact]
    public void Wrong_password_does_not_verify()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple");
        PasswordHasher.Verify("Tr0ub4dor&3", hash).Should().BeFalse();
    }

    [Fact]
    public void Two_hashes_of_the_same_password_differ_because_of_the_salt()
    {
        var a = PasswordHasher.Hash("same-password");
        var b = PasswordHasher.Hash("same-password");
        a.Should().NotBe(b);
        // ...but both verify against the original password.
        PasswordHasher.Verify("same-password", a).Should().BeTrue();
        PasswordHasher.Verify("same-password", b).Should().BeTrue();
    }

    [Fact]
    public void Encoded_hash_is_self_describing_argon2id_phc_format()
    {
        var hash = PasswordHasher.Hash("x");
        // $argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>
        hash.Should().StartWith("$argon2id$v=19$m=19456,t=2,p=1$");
        hash.Split('$').Should().HaveCount(6);
    }

    [Fact]
    public void A_malformed_stored_hash_verifies_false_rather_than_throwing()
    {
        PasswordHasher.Verify("anything", "not-a-phc-hash").Should().BeFalse();
        PasswordHasher.Verify("anything", "").Should().BeFalse();
        PasswordHasher.Verify("anything", "$argon2id$v=19$garbage").Should().BeFalse();
    }
}
