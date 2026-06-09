using Microsoft.AspNetCore.Authorization;
using Packet.Node.Core.Auth;

namespace Packet.Node.Api;

/// <summary>
/// The web control-API authorization policies and the scope requirement behind
/// them — the per-endpoint gates that enforce the <c>read</c>/<c>operate</c>/<c>admin</c>
/// scopes when <c>management.auth.enabled</c> is on, and pass through entirely when
/// it is off.
/// </summary>
/// <remarks>
/// <para>
/// <b>Default-off, no regression.</b> The whole gate hinges on
/// <see cref="ScopeRequirementHandler"/>: when auth is disabled it <em>succeeds
/// unconditionally</em> (no token, no claim, no challenge), so a node with auth off
/// serves every endpoint exactly as it did before auth existed. Enforcement only
/// engages when the flag is on. The flag is read live from the config provider on
/// each request, so flipping it does not require re-registering policies.
/// </para>
/// <para>
/// <b>Scope implication (admin ⊃ operate ⊃ read)</b> is applied here via
/// <see cref="AuthScopes.Satisfies"/> — a single <c>scope</c> claim on the token is
/// compared by rank against the endpoint's required scope, so an <c>admin</c> token
/// passes a <c>read</c> gate without carrying a <c>read</c> claim.
/// </para>
/// </remarks>
public static class PdnAuthPolicies
{
    /// <summary>Policy name for the read-scope gate.</summary>
    public const string Read = "pdn-read";

    /// <summary>Policy name for the operate-scope gate.</summary>
    public const string Operate = "pdn-operate";

    /// <summary>Policy name for the admin-scope gate.</summary>
    public const string Admin = "pdn-admin";

    /// <summary>Register the three scope policies on the options.</summary>
    public static void AddPdnScopePolicies(this AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddPolicy(Read, p => p.AddRequirements(new ScopeRequirement(AuthScopes.Read)));
        options.AddPolicy(Operate, p => p.AddRequirements(new ScopeRequirement(AuthScopes.Operate)));
        options.AddPolicy(Admin, p => p.AddRequirements(new ScopeRequirement(AuthScopes.Admin)));
    }
}

/// <summary>An endpoint's required scope (one of <see cref="AuthScopes"/>).</summary>
public sealed class ScopeRequirement(string requiredScope) : IAuthorizationRequirement
{
    /// <summary>The scope this endpoint requires.</summary>
    public string RequiredScope { get; } = requiredScope;
}
