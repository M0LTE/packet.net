namespace Packet.Node.Core.Auth.Oauth;

/// <summary>
/// A single-use OAuth authorization code (OAuth 2.1 code + PKCE). Bound to the client,
/// the exact redirect URI, the PKCE challenge, the granted scope, the requested resource,
/// and the authenticated owner — all re-checked at the token endpoint. Short TTL.
/// </summary>
/// <param name="Code">The opaque code value handed to the client via the redirect.</param>
/// <param name="ClientId">The client the code was issued to (must match at token exchange).</param>
/// <param name="RedirectUri">The exact redirect URI used (must match at token exchange).</param>
/// <param name="CodeChallenge">The PKCE S256 challenge; the verifier is checked against it.</param>
/// <param name="Scope">The granted node scope claim (<c>read</c>/<c>operate</c>).</param>
/// <param name="Resource">The RFC 8707 resource the client requested (recorded; the /mcp URL).</param>
/// <param name="Username">The owner who authenticated + consented.</param>
/// <param name="ExpiresUtc">When the code stops being redeemable.</param>
public sealed record OauthCode(
    string Code,
    string ClientId,
    string RedirectUri,
    string CodeChallenge,
    string Scope,
    string Resource,
    string Username,
    DateTimeOffset ExpiresUtc);

/// <summary>
/// The single-use authorization-code store. <see cref="Consume"/> is atomic (delete + return
/// in one statement) so a code can be redeemed exactly once — a replay finds nothing. Resilient
/// on store fault (logs + degrades to null/no-op).
/// </summary>
public interface IOauthCodeStore
{
    /// <summary>Persist a freshly-minted code.</summary>
    void Issue(OauthCode code);

    /// <summary>Atomically consume <paramref name="code"/>: delete it and return it if it
    /// existed and has not expired as of <paramref name="now"/>; otherwise null. A second
    /// call for the same code returns null (single-use).</summary>
    OauthCode? Consume(string code, DateTimeOffset now);
}
