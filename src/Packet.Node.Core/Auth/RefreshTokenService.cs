using System.Security.Cryptography;
using System.Text;

namespace Packet.Node.Core.Auth;

/// <summary>
/// The refresh-token lifecycle on top of <see cref="IRefreshTokenStore"/>: issue
/// (on login), rotate (one-time-use exchange), reuse detection (the theft
/// response), and logout. Web-free (it depends only on the store + a
/// <see cref="TimeProvider"/>), so it lives in <c>Packet.Node.Core</c> and is unit
/// testable on <c>FakeTimeProvider</c>; the host wires the
/// <c>/auth/refresh</c> + <c>/auth/logout</c> endpoints around it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Opaque token, hash at rest.</b> The token handed to the client is a 256-bit
/// CSPRNG value (<see cref="RandomNumberGenerator.GetBytes(int)"/>, 32 bytes,
/// base64url) — unguessable and never derived from anything. Only its SHA-256 hash
/// is stored, so a database read never yields a usable token, exactly like a
/// password is never stored in clear.
/// </para>
/// <para>
/// <b>One-time use + rotation.</b> Each refresh token is good for exactly one
/// rotation: <see cref="Rotate"/> marks the presented token revoked and mints a new
/// one in the <em>same family</em>. A family is the chain of tokens descending from
/// one login.
/// </para>
/// <para>
/// <b>Reuse detection (theft response).</b> A token that is found but
/// <em>already revoked</em> means someone replayed a consumed token — either the
/// legitimate client racing itself, or an attacker who stole a token that the
/// client has since rotated past. We cannot tell which, so we take the safe action:
/// <see cref="RevokeFamily"/> the entire family (logging out every descendant) and
/// reject. This bounds a stolen-token window to a single rotation.
/// </para>
/// <para>
/// <b>No wall-clock (repo rule §2.7):</b> issue/expiry stamps and the
/// expiry comparison ride the injected <see cref="TimeProvider"/>.
/// </para>
/// </remarks>
public sealed class RefreshTokenService
{
    private const int TokenBytes = 32;   // 256-bit opaque token

    private readonly IRefreshTokenStore store;
    private readonly TimeProvider clock;
    private readonly TimeSpan lifetime;

    /// <summary>
    /// Construct over the backing store, the refresh-token lifetime, and the clock.
    /// </summary>
    /// <param name="store">The hash-only persistence seam.</param>
    /// <param name="lifetime">How long a fresh refresh token lives (login instant +
    /// this). Must be positive.</param>
    /// <param name="clock">The injected clock (issue/expiry + the expiry check ride
    /// this — no wall-clock).</param>
    public RefreshTokenService(IRefreshTokenStore store, TimeSpan lifetime, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Refresh-token lifetime must be positive.");
        }
        this.store = store;
        this.lifetime = lifetime;
        this.clock = clock;
    }

    /// <summary>
    /// Issue a brand-new refresh token for <paramref name="username"/> in a fresh
    /// random family (the start of a rotation chain — call this on every login).
    /// Returns the opaque token the client must keep, or null if the store could not
    /// persist it (the caller then declines to hand out a refresh token, but the
    /// login itself can still succeed with just the access token).
    /// </summary>
    public string? Issue(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        var family = NewOpaque();   // a random family id (also a 256-bit value)
        return MintInFamily(username, family);
    }

    /// <summary>
    /// Rotate a presented refresh token. Looks it up by hash and applies the
    /// one-time-use + reuse-detection rules:
    /// <list type="bullet">
    /// <item><b>Not found</b> → <see cref="RefreshOutcome.Invalid"/> (unknown/forged token).</item>
    /// <item><b>Already revoked</b> → revoke the whole family and return
    /// <see cref="RefreshOutcome.ReuseDetected"/> (theft response).</item>
    /// <item><b>Expired</b> → <see cref="RefreshOutcome.Expired"/>.</item>
    /// <item><b>Valid</b> → revoke it, mint a successor in the same family, return
    /// <see cref="RefreshOutcome.Rotated"/> with the new opaque token + the username.</item>
    /// </list>
    /// All four outcomes are 401 to the client; the distinction drives the audit log.
    /// </summary>
    public RefreshResult Rotate(string presentedToken)
    {
        if (string.IsNullOrEmpty(presentedToken))
        {
            return RefreshResult.Failure(RefreshOutcome.Invalid);
        }

        var hash = HashToken(presentedToken);
        var record = store.FindByHash(hash);
        if (record is null)
        {
            // Unknown token: forged, already-pruned, or never ours. No family to act on.
            return RefreshResult.Failure(RefreshOutcome.Invalid);
        }

        if (record.Revoked)
        {
            // A consumed/revoked token was replayed → assume theft and burn the family.
            store.RevokeFamily(record.Family);
            return RefreshResult.Failure(RefreshOutcome.ReuseDetected, record.Username, record.Family);
        }

        if (clock.GetUtcNow() >= record.ExpiresUtc)
        {
            // Expired but still valid-looking: revoke it so it can't later be replayed
            // as a "reuse" false-positive, and reject. (Pruning eventually removes it.)
            store.Revoke(hash);
            return RefreshResult.Failure(RefreshOutcome.Expired, record.Username, record.Family);
        }

        // Valid: consume it (one-time use) and mint a successor in the SAME family.
        store.Revoke(hash);
        var next = MintInFamily(record.Username, record.Family);
        if (next is null)
        {
            // The successor couldn't be persisted; we've already revoked the presented
            // one, so the session can't silently continue on a stale token. Surface a
            // store fault as Invalid (401) rather than hand back an untracked token.
            return RefreshResult.Failure(RefreshOutcome.Invalid, record.Username, record.Family);
        }
        return RefreshResult.Success(next, record.Username, record.Family);
    }

    /// <summary>
    /// Revoke the family of a presented token (logout). Best-effort: an unknown
    /// token is a no-op success (logout is idempotent — there is nothing to leak by
    /// confirming it). Returns the username + family acted on when known, for the
    /// audit log.
    /// </summary>
    public (string? Username, string? Family) Logout(string presentedToken)
    {
        if (string.IsNullOrEmpty(presentedToken))
        {
            return (null, null);
        }
        var record = store.FindByHash(HashToken(presentedToken));
        if (record is null)
        {
            return (null, null);
        }
        store.RevokeFamily(record.Family);
        return (record.Username, record.Family);
    }

    /// <summary>Revoke a family directly (used when a refresh finds the family's user
    /// has been deleted — we burn the family rather than mint for a non-existent
    /// user). Best-effort.</summary>
    public void LogoutFamily(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        store.RevokeFamily(family);
    }

    /// <summary>Best-effort prune of tokens already expired as of now. Safe to call
    /// opportunistically (e.g. on each login); a fault is swallowed by the store.</summary>
    public void PruneExpired() => store.PruneExpired(clock.GetUtcNow());

    // Mint + persist a token in the given family; null if the store rejected the write.
    private string? MintInFamily(string username, string family)
    {
        var token = NewOpaque();
        var now = clock.GetUtcNow();
        var record = new RefreshTokenRecord(
            HashToken(token), username, family, now, now + lifetime, Revoked: false);
        return store.Insert(record) ? token : null;
    }

    // A fresh 256-bit CSPRNG value, base64url-encoded (URL-safe, unpadded) so it can
    // travel in a JSON body / header without escaping.
    private static string NewOpaque() =>
        Base64Url(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>
    /// SHA-256 of the opaque token, base64url-encoded — the value stored + looked up.
    /// A plain hash (no salt) is correct here: the input is already 256 bits of
    /// CSPRNG entropy, so there is nothing to brute-force and a per-token salt would
    /// only break the by-hash lookup. (Contrast passwords, which are low-entropy and
    /// need Argon2 + per-user salt.)
    /// </summary>
    public static string HashToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Base64Url(digest);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>The outcome of a <see cref="RefreshTokenService.Rotate"/> call.</summary>
public enum RefreshOutcome
{
    /// <summary>The presented token was valid; a successor was minted (see
    /// <see cref="RefreshResult.NewToken"/>).</summary>
    Rotated,

    /// <summary>The token is unknown / forged / never ours (or a store fault on the
    /// successor write). 401.</summary>
    Invalid,

    /// <summary>The token had expired. 401.</summary>
    Expired,

    /// <summary>The token was found already-revoked — a replay of a consumed token.
    /// The whole family was revoked as the theft response. 401.</summary>
    ReuseDetected,
}

/// <summary>
/// The result of a rotation. On <see cref="RefreshOutcome.Rotated"/>,
/// <see cref="NewToken"/> + <see cref="Username"/> are set; otherwise it carries the
/// failure outcome (and, when the token was found, the username/family for the audit
/// log).
/// </summary>
public sealed record RefreshResult(
    RefreshOutcome Outcome,
    string? NewToken,
    string? Username,
    string? Family)
{
    /// <summary>Whether the rotation produced a new usable token.</summary>
    public bool IsSuccess => Outcome == RefreshOutcome.Rotated;

    internal static RefreshResult Success(string newToken, string username, string family) =>
        new(RefreshOutcome.Rotated, newToken, username, family);

    internal static RefreshResult Failure(RefreshOutcome outcome, string? username = null, string? family = null) =>
        new(outcome, null, username, family);
}
