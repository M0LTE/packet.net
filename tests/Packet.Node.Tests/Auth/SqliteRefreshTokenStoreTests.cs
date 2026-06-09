using Packet.Node.Core.Auth;

namespace Packet.Node.Tests.Auth;

/// <summary>
/// Round-trips the SQLite refresh-token store on a temp db: insert/find by hash,
/// single-token revoke, whole-family revoke, prune of expired tokens, persistence
/// across a reopen, and the degrade-not-throw resilience of a broken store.
/// </summary>
[Trait("Category", "Node")]
public sealed class SqliteRefreshTokenStoreTests : IDisposable
{
    private readonly string dir;
    private readonly string dbPath;

    public SqliteRefreshTokenStoreTests()
    {
        dir = Path.Combine(Path.GetTempPath(), "packetnet-rtstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        dbPath = Path.Combine(dir, "pdn.db");
    }

    private SqliteRefreshTokenStore Open() => new(dbPath);

    private static RefreshTokenRecord NewToken(string hash, string family, bool revoked = false,
        DateTimeOffset? expires = null) =>
        new(hash, "m0lte", family, DateTimeOffset.UnixEpoch,
            expires ?? new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), revoked);

    [Fact]
    public void Insert_then_find_by_hash_round_trips()
    {
        var store = Open();
        store.Insert(NewToken("hash-1", "famA")).Should().BeTrue();

        var found = store.FindByHash("hash-1");
        found.Should().NotBeNull();
        found!.TokenHash.Should().Be("hash-1");
        found.Family.Should().Be("famA");
        found.Username.Should().Be("m0lte");
        found.Revoked.Should().BeFalse();

        store.FindByHash("nope").Should().BeNull();
    }

    [Fact]
    public void Revoke_marks_a_single_token()
    {
        var store = Open();
        store.Insert(NewToken("hash-1", "famA")).Should().BeTrue();
        store.Insert(NewToken("hash-2", "famA")).Should().BeTrue();

        store.Revoke("hash-1").Should().BeTrue();
        store.FindByHash("hash-1")!.Revoked.Should().BeTrue();
        store.FindByHash("hash-2")!.Revoked.Should().BeFalse();   // sibling untouched

        store.Revoke("absent").Should().BeFalse();
    }

    [Fact]
    public void RevokeFamily_marks_every_unrevoked_row_in_the_family()
    {
        var store = Open();
        store.Insert(NewToken("a1", "famA")).Should().BeTrue();
        store.Insert(NewToken("a2", "famA")).Should().BeTrue();
        store.Insert(NewToken("b1", "famB")).Should().BeTrue();

        store.RevokeFamily("famA").Should().Be(2);
        store.FindByHash("a1")!.Revoked.Should().BeTrue();
        store.FindByHash("a2")!.Revoked.Should().BeTrue();
        store.FindByHash("b1")!.Revoked.Should().BeFalse();       // other family untouched

        // Idempotent — re-revoking an already-revoked family changes 0 rows.
        store.RevokeFamily("famA").Should().Be(0);
    }

    [Fact]
    public void PruneExpired_removes_only_already_expired_tokens()
    {
        var store = Open();
        var cutoff = new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);
        store.Insert(NewToken("old", "famA", expires: cutoff - TimeSpan.FromDays(1))).Should().BeTrue();
        store.Insert(NewToken("fresh", "famA", expires: cutoff + TimeSpan.FromDays(1))).Should().BeTrue();

        store.PruneExpired(cutoff).Should().Be(1);
        store.FindByHash("old").Should().BeNull();
        store.FindByHash("fresh").Should().NotBeNull();
    }

    [Fact]
    public void Data_persists_across_a_reopen()
    {
        Open().Insert(NewToken("hash-1", "famA")).Should().BeTrue();

        var reopened = Open();
        reopened.FindByHash("hash-1").Should().NotBeNull();
    }

    [Fact]
    public void A_broken_store_degrades_and_never_throws()
    {
        // A db path under a non-existent directory can't be opened → the schema init
        // logs + degrades, and every op returns the safe default rather than throwing.
        var broken = new SqliteRefreshTokenStore(Path.Combine(dir, "no-such-dir", "pdn.db"));

        broken.Insert(NewToken("h", "f")).Should().BeFalse();
        broken.FindByHash("h").Should().BeNull();
        broken.Revoke("h").Should().BeFalse();
        broken.RevokeFamily("f").Should().Be(0);
        broken.PruneExpired(DateTimeOffset.UnixEpoch).Should().Be(0);
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }
}
