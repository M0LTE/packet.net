using Packet.Node.Core.Auth;

namespace Packet.Node.Tests.Auth;

/// <summary>
/// Round-trips the SQLite user store on a temp db: create/find/delete, the unique
/// username constraint, count, last-login stamp, and the persisted signing key
/// (stable across reopens, 256-bit).
/// </summary>
[Trait("Category", "Node")]
public sealed class SqliteUserStoreTests : IDisposable
{
    private readonly string dir;
    private readonly string dbPath;

    public SqliteUserStoreTests()
    {
        dir = Path.Combine(Path.GetTempPath(), "packetnet-userstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        dbPath = Path.Combine(dir, "pdn.db");
    }

    private SqliteUserStore Open() => new(dbPath);

    private static UserRecord NewUser(string name, string scope = AuthScopes.Read) =>
        new(name, PasswordHasher.Hash("pw-" + name), scope, DateTimeOffset.UnixEpoch, null);

    [Fact]
    public void Empty_store_counts_zero()
    {
        var store = Open();
        store.Count().Should().Be(0);
        store.FindByUsername("nobody").Should().BeNull();
    }

    [Fact]
    public void Create_then_find_round_trips()
    {
        var store = Open();
        var user = NewUser("alice", AuthScopes.Admin);
        store.Create(user).Should().BeTrue();

        store.Count().Should().Be(1);
        var found = store.FindByUsername("alice");
        found.Should().NotBeNull();
        found!.Username.Should().Be("alice");
        found.Scope.Should().Be(AuthScopes.Admin);
        found.PasswordHash.Should().Be(user.PasswordHash);
        found.LastLoginUtc.Should().BeNull();
    }

    [Fact]
    public void Duplicate_username_is_rejected()
    {
        var store = Open();
        store.Create(NewUser("bob")).Should().BeTrue();
        store.Create(NewUser("bob", AuthScopes.Admin)).Should().BeFalse();
        store.Count().Should().Be(1);
    }

    [Fact]
    public void Delete_removes_the_user()
    {
        var store = Open();
        store.Create(NewUser("carol")).Should().BeTrue();
        store.Delete("carol").Should().BeTrue();
        store.Count().Should().Be(0);
        store.Delete("carol").Should().BeFalse();   // already gone
    }

    [Fact]
    public void Update_last_login_stamps_the_user()
    {
        var store = Open();
        store.Create(NewUser("dave")).Should().BeTrue();
        var when = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
        store.UpdateLastLogin("dave", when);

        store.FindByUsername("dave")!.LastLoginUtc.Should().Be(when);
    }

    [Fact]
    public void List_returns_all_users_without_dropping_hashes()
    {
        var store = Open();
        store.Create(NewUser("a")).Should().BeTrue();
        store.Create(NewUser("b")).Should().BeTrue();

        var all = store.List();
        all.Should().HaveCount(2);
        all.Should().OnlyContain(u => !string.IsNullOrEmpty(u.PasswordHash));
    }

    [Fact]
    public void Signing_key_is_256_bit_and_stable_across_reopens()
    {
        var key1 = Open().GetOrCreateSigningKey();
        key1.Should().NotBeNull();
        key1!.Length.Should().Be(32);   // 256 bits

        // Reopen the same db → same persisted key (tokens survive a restart).
        var key2 = Open().GetOrCreateSigningKey();
        key2.Should().Equal(key1);
    }

    [Fact]
    public void Data_persists_across_a_reopen()
    {
        Open().Create(NewUser("ed", AuthScopes.Operate)).Should().BeTrue();

        var reopened = Open();
        reopened.Count().Should().Be(1);
        reopened.FindByUsername("ed")!.Scope.Should().Be(AuthScopes.Operate);
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }
}
