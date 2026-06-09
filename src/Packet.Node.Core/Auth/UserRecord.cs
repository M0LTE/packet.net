namespace Packet.Node.Core.Auth;

/// <summary>
/// One web control-API user as persisted in <c>pdn.db</c>. The
/// <see cref="PasswordHash"/> is the full self-describing Argon2id encoded hash
/// (algorithm, parameters, salt and digest in one string — see
/// <see cref="PasswordHasher"/>); it is never returned to a client.
/// </summary>
/// <param name="Username">Unique, case-sensitive login name.</param>
/// <param name="PasswordHash">The full encoded Argon2id hash (params + salt + digest).</param>
/// <param name="Scope">The granted scope: one of <see cref="AuthScopes.Read"/> /
/// <see cref="AuthScopes.Operate"/> / <see cref="AuthScopes.Admin"/>.</param>
/// <param name="CreatedUtc">When the user was created.</param>
/// <param name="LastLoginUtc">When the user last successfully logged in, or null.</param>
public sealed record UserRecord(
    string Username,
    string PasswordHash,
    string Scope,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? LastLoginUtc);

/// <summary>
/// A user projected for the API — the hash is deliberately absent. This is the
/// only shape <c>/users</c> returns; <see cref="UserRecord.PasswordHash"/> never
/// leaves the store.
/// </summary>
public sealed record UserSummary(
    string Username,
    string Scope,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? LastLoginUtc)
{
    /// <summary>Project a <see cref="UserRecord"/> to its hash-free summary.</summary>
    public static UserSummary From(UserRecord user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new UserSummary(user.Username, user.Scope, user.CreatedUtc, user.LastLoginUtc);
    }
}
