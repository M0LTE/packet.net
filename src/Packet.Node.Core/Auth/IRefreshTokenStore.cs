namespace Packet.Node.Core.Auth;

/// <summary>
/// The persistence seam for web control-API refresh tokens, kept web-free so it
/// lives in <c>Packet.Node.Core</c> (the host wires the rotation endpoints around
/// the <see cref="RefreshTokenService"/> that drives it).
/// </summary>
/// <remarks>
/// <para>
/// Resilient like <see cref="IUserStore"/> and the NET/ROM routing store: a
/// backing-store fault logs and degrades (a lookup returns null, a write returns
/// false) — it never throws out to crash the node. Implementations open a fresh
/// pooled connection per call.
/// </para>
/// <para>
/// <b>Only hashes are stored.</b> Callers pass the SHA-256 hash of the opaque
/// token (see <see cref="RefreshTokenRecord.TokenHash"/>); the plaintext token
/// never reaches the store. Lookup is by hash — exactly like a password is never
/// stored in clear and is verified, never read back.
/// </para>
/// </remarks>
public interface IRefreshTokenStore
{
    /// <summary>Insert a freshly-minted refresh token. Returns <c>false</c> on a
    /// store fault (the caller then fails the issue safely — no token returned).</summary>
    bool Insert(RefreshTokenRecord token);

    /// <summary>Look up a token by its SHA-256 hash, or null if absent / on fault.</summary>
    RefreshTokenRecord? FindByHash(string tokenHash);

    /// <summary>Mark a single token revoked (consumed on rotation, or logged out).
    /// Returns <c>true</c> if a row changed, <c>false</c> if absent or on fault.</summary>
    bool Revoke(string tokenHash);

    /// <summary>Revoke every token in a family — the theft response (a replayed,
    /// already-revoked token) and the logout path. Returns the number of rows
    /// revoked (0 on fault / empty family).</summary>
    int RevokeFamily(string family);

    /// <summary>Best-effort prune of tokens that expired before
    /// <paramref name="olderThanUtc"/>, so the table doesn't grow without bound. A
    /// fault is swallowed (pruning never fails an operation). Returns rows removed.</summary>
    int PruneExpired(DateTimeOffset olderThanUtc);
}
