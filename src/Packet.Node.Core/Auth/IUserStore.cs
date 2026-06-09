namespace Packet.Node.Core.Auth;

/// <summary>
/// The persistence seam for web control-API users and the JWT signing key, kept
/// web-free so it lives in <c>Packet.Node.Core</c> (the host wires the JWT
/// middleware + endpoints around it).
/// </summary>
/// <remarks>
/// Resilient like the NET/ROM routing store: a backing-store fault logs and
/// degrades (a query returns null/empty, a write returns false) — it never throws
/// out to crash the node. Implementations open a fresh pooled connection per call.
/// </remarks>
public interface IUserStore
{
    /// <summary>The number of users. <c>0</c> means first-run setup is still
    /// required. Returns <c>0</c> on a store fault (so a broken store reads as
    /// "needs setup" rather than locking everyone out — see the implementation
    /// note).</summary>
    int Count();

    /// <summary>Look up a user by exact username, or null if absent / on fault.</summary>
    UserRecord? FindByUsername(string username);

    /// <summary>All users (hash included — callers project to <see cref="UserSummary"/>
    /// before returning to a client). Empty on fault.</summary>
    IReadOnlyList<UserRecord> List();

    /// <summary>
    /// Create a user. Returns <c>false</c> if the username already exists
    /// (UNIQUE violation) or on a store fault; <c>true</c> on success.
    /// </summary>
    bool Create(UserRecord user);

    /// <summary>Delete a user by username. Returns <c>true</c> if a row was
    /// removed, <c>false</c> if absent or on fault.</summary>
    bool Delete(string username);

    /// <summary>Stamp a user's last-login time. Best-effort: a fault is swallowed
    /// (a failed last-login update must never fail an otherwise-good login).</summary>
    void UpdateLastLogin(string username, DateTimeOffset whenUtc);

    /// <summary>
    /// The persisted 256-bit JWT signing key, generating + storing it on first
    /// call so tokens survive a restart. Returns null only if the store is so
    /// broken it can neither read nor persist a key (auth then cannot be enabled
    /// safely — the host treats a null key as "auth unavailable"). Never logged.
    /// </summary>
    byte[]? GetOrCreateSigningKey();
}
