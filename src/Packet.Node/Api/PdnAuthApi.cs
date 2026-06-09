using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Packet.Node.Core.Auth;
using Packet.Node.Core.Configuration;

namespace Packet.Node.Api;

/// <summary>
/// The authentication side of the pdn node control API: login, the first-run setup
/// probe + bootstrap, and user management. New in the auth foundation pass.
/// </summary>
/// <remarks>
/// <para>
/// <b>Always-open (never gated):</b> <c>GET /setup/state</c> and
/// <c>POST /auth/login</c> + <c>POST /setup</c> are reachable without a token — they
/// are the bootstrap path (you cannot present a token before you have an account).
/// They are mapped here without any <c>.RequireAuthorization</c>. <c>/users</c> is
/// gated <c>admin</c> (via the conditional gate in <c>Program.cs</c>, so it too is
/// open when auth is disabled).
/// </para>
/// <para>
/// <b>Login timing-safety.</b> A login for an unknown username and a login with a
/// bad password take the same code path and the same time: an unknown user is
/// verified against a fixed decoy Argon2 hash so the (expensive, dominant)
/// Argon2 derivation runs either way, and both failures return the identical
/// generic 401 — no oracle for "does this user exist?".
/// </para>
/// <para>
/// <b>Setup is one-shot:</b> <c>POST /setup</c> only succeeds while zero users
/// exist; once an admin exists it returns 409. It creates the admin
/// (<c>admin</c> scope) and applies the station identity (+ optional first port)
/// through the existing <see cref="IWritableConfigProvider.TryApply"/> seam — the
/// same validate→persist→reconcile path the config editor uses — rather than
/// reinventing a config write.
/// </para>
/// <para>
/// No wall-clock (repo rule §2.7): the injected <see cref="TimeProvider"/> stamps
/// <c>created</c>/<c>last login</c> and drives the token expiry through
/// <see cref="JwtTokenService"/>.
/// </para>
/// </remarks>
public static class PdnAuthApi
{
    // Minimum admin/user password length enforced on setup + user-create. A floor,
    // not a policy engine — keep the bar simple but non-trivial.
    private const int MinPasswordLength = 8;

    // A fixed, well-formed decoy Argon2id hash verified against when the username is
    // unknown, so an unknown-user login still pays the full Argon2 cost (constant-time
    // w.r.t. user existence). Generated once at module load from a random password the
    // caller can never know — its only purpose is to burn the same CPU as a real verify.
    private static readonly string DecoyHash = PasswordHasher.Hash(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Map the auth endpoints under <c>/api/v1</c>. Called from the node composition
    /// root. The login / setup endpoints are always open; the <c>/users</c> group is
    /// returned to the caller so it can apply the admin gate conditionally on the
    /// auth flag (the same way every other gated group is wired).
    /// </summary>
    /// <returns>The <c>/users</c> route group, for the caller to gate <c>admin</c>.</returns>
    public static RouteGroupBuilder MapPdnAuthApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var v1 = app.MapGroup("/api/v1");

        // --- Always-open bootstrap endpoints --------------------------------------

        // Whether first-run setup is still required (zero users). Unauthenticated —
        // it is the probe the setup wizard hits before any account exists.
        v1.MapGet("/setup/state", (IUserStore users) =>
            Results.Ok(new SetupStateResponse(NeedsSetup: users.Count() == 0)));

        // Password login → JWT. Generic 401 on any failure; same timing for
        // unknown-user vs bad-password (see the type remarks).
        // [FromServices] on the nullable token service: when the signing key is
        // unavailable the service is unregistered, and an explicit optional-service
        // binding resolves it to null (→ 503 below) instead of failing minimal-API
        // parameter inference at startup (which would abort the whole host).
        v1.MapPost("/auth/login", (LoginRequest body, IUserStore users, [Microsoft.AspNetCore.Mvc.FromServices] JwtTokenService? tokens, TimeProvider clock) =>
        {
            if (tokens is null)
            {
                // Auth couldn't be initialised (e.g. the signing key is unreadable).
                return Results.Problem("Authentication is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            if (body is null || string.IsNullOrEmpty(body.Username) || string.IsNullOrEmpty(body.Password))
            {
                return Unauthorized();
            }

            var user = users.FindByUsername(body.Username);
            // Constant-time w.r.t. user existence: verify against the decoy when the
            // user is absent so the Argon2 derivation runs either way, then fail
            // generically. (FixedTimeEquals inside Verify guards the digest compare.)
            bool ok = user is not null
                ? PasswordHasher.Verify(body.Password, user.PasswordHash)
                : PasswordHasher.Verify(body.Password, DecoyHash) && false;

            if (!ok || user is null)
            {
                return Unauthorized();
            }

            var (token, expiresAt) = tokens.Issue(user.Username, user.Scope);
            users.UpdateLastLogin(user.Username, clock.GetUtcNow());
            return Results.Ok(new LoginResponse(token, expiresAt, user.Scope));
        });

        // First-run bootstrap: create the admin + apply identity/firstPort. One-shot.
        v1.MapPost("/setup", (SetupRequest body, IUserStore users, IWritableConfigProvider cfg, TimeProvider clock) =>
        {
            // One-shot: refuse once any user exists (403 — the bootstrap is over).
            if (users.Count() > 0)
            {
                return Results.Problem("Setup has already been completed.", statusCode: StatusCodes.Status403Forbidden);
            }
            if (body is null || body.Identity is null || body.Admin is null)
            {
                return Results.BadRequest(new { error = "identity and admin are required." });
            }
            if (string.IsNullOrWhiteSpace(body.Identity.Callsign))
            {
                return Results.BadRequest(new { error = "identity.callsign is required." });
            }
            if (string.IsNullOrWhiteSpace(body.Admin.Username))
            {
                return Results.BadRequest(new { error = "admin.username is required." });
            }
            if (body.Admin.Password is null || body.Admin.Password.Length < MinPasswordLength)
            {
                return Results.BadRequest(new { error = $"admin.password must be at least {MinPasswordLength} characters." });
            }

            // Build the candidate config from the live one + the setup identity (+ first
            // port if given), then push it through the SAME write seam the editor uses —
            // it validates the callsign/port and reconciles. A rejected config (e.g. a
            // malformed callsign) is a 422, and NO user is created (config first).
            var current = cfg.Current;
            var candidate = current with
            {
                Identity = new Identity
                {
                    Callsign = body.Identity.Callsign,
                    Alias = string.IsNullOrWhiteSpace(body.Identity.Alias) ? null : body.Identity.Alias,
                    Grid = string.IsNullOrWhiteSpace(body.Identity.Grid) ? null : body.Identity.Grid,
                },
            };
            if (body.FirstPort is { } port)
            {
                candidate = candidate with { Ports = [.. current.Ports, port] };
            }

            if (!cfg.TryApply(candidate, out var errors))
            {
                return Results.UnprocessableEntity(new Packet.Node.Core.Api.ValidationProblem(errors));
            }

            // Config applied → create the admin user. Guarded by Create's UNIQUE +
            // the zero-users check above; a Create-false here means a concurrent setup
            // raced us in (still one-shot overall) → 409.
            var now = clock.GetUtcNow();
            var admin = new UserRecord(
                body.Admin.Username.Trim(),
                PasswordHasher.Hash(body.Admin.Password),
                AuthScopes.Admin,
                now,
                LastLoginUtc: null);
            if (!users.Create(admin))
            {
                return Results.Conflict(new { error = "An administrator already exists." });
            }

            return Results.Ok(new SetupResponse(admin.Username, admin.Scope));
        });

        // --- Admin-gated user management (gated by the caller) --------------------

        var usersGroup = v1.MapGroup("/users");

        usersGroup.MapGet("", (IUserStore users) =>
            Results.Ok(users.List().Select(UserSummary.From).ToArray()));

        usersGroup.MapPost("", (CreateUserRequest body, IUserStore users, TimeProvider clock) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.Username))
            {
                return Results.BadRequest(new { error = "username is required." });
            }
            if (body.Password is null || body.Password.Length < MinPasswordLength)
            {
                return Results.BadRequest(new { error = $"password must be at least {MinPasswordLength} characters." });
            }
            if (!AuthScopes.IsKnown(body.Scope))
            {
                return Results.BadRequest(new { error = $"scope must be one of: {AuthScopes.Read}, {AuthScopes.Operate}, {AuthScopes.Admin}." });
            }

            var user = new UserRecord(
                body.Username.Trim(),
                PasswordHasher.Hash(body.Password),
                body.Scope!,
                clock.GetUtcNow(),
                LastLoginUtc: null);
            if (!users.Create(user))
            {
                return Results.Conflict(new { error = $"User '{user.Username}' already exists." });
            }
            return Results.Created($"/api/v1/users/{user.Username}", UserSummary.From(user));
        });

        usersGroup.MapDelete("/{username}", (string username, IUserStore users) =>
        {
            // Don't let the last admin delete themselves into a locked-out node.
            var all = users.List();
            var target = all.FirstOrDefault(u => u.Username == username);
            if (target is null)
            {
                return Results.NotFound();
            }
            if (target.Scope == AuthScopes.Admin && all.Count(u => u.Scope == AuthScopes.Admin) <= 1)
            {
                return Results.Conflict(new { error = "Cannot delete the last administrator." });
            }
            return users.Delete(username) ? Results.NoContent() : Results.NotFound();
        });

        return usersGroup;
    }

    // The identical generic 401 every login failure returns — no detail on which of
    // username/password was wrong.
    private static IResult Unauthorized() =>
        Results.Json(new { error = "Invalid username or password." }, statusCode: StatusCodes.Status401Unauthorized);

    // --- Request / response DTOs (camelCased by STJ web defaults) ----------------

    /// <summary>The <c>/auth/login</c> request body.</summary>
    public sealed record LoginRequest(string Username, string Password);

    /// <summary>The <c>/auth/login</c> success body.</summary>
    public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, string Scopes);

    /// <summary>The <c>/setup/state</c> body.</summary>
    public sealed record SetupStateResponse(bool NeedsSetup);

    /// <summary>The <c>/setup</c> request body.</summary>
    public sealed record SetupRequest(SetupIdentity Identity, SetupAdmin Admin, PortConfig? FirstPort = null);

    /// <summary>The station identity supplied at setup.</summary>
    public sealed record SetupIdentity(string Callsign, string? Alias = null, string? Grid = null);

    /// <summary>The first admin account supplied at setup.</summary>
    public sealed record SetupAdmin(string Username, string Password);

    /// <summary>The <c>/setup</c> success body.</summary>
    public sealed record SetupResponse(string Username, string Scope);

    /// <summary>The <c>POST /users</c> request body.</summary>
    public sealed record CreateUserRequest(string Username, string Password, string Scope);
}
