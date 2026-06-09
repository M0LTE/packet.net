using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Packet.Node.Core.Auth;

/// <summary>
/// The SQLite-backed <see cref="IRefreshTokenStore"/>: persists refresh tokens (by
/// hash only) to the same consolidated <c>pdn.db</c> the users + routing table live
/// in. Raw SQL via Dapper, mirroring <see cref="SqliteUserStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Schema</b> is created with <c>CREATE TABLE IF NOT EXISTS</c> — the same
/// meta-row approach the user store uses, so it doesn't fight the routing store
/// over <c>PRAGMA user_version</c>. One table, <c>refresh_token</c>, keyed by the
/// token hash, with an index on <c>family</c> for the whole-family revoke.
/// </para>
/// <para>
/// <b>Only the hash is stored</b> (never the opaque token), exactly the way the
/// user store never holds a clear password. The token is a 256-bit CSPRNG value
/// the client holds; we keep its SHA-256 and look up by it.
/// </para>
/// <para>
/// <b>Resilient.</b> Every operation is wrapped: a schema/open failure logs and
/// leaves the node running (refresh simply can't be used); a lookup returns null
/// on fault, a write returns false / 0. Persistence can never take the node down.
/// WAL mode, a fresh pooled connection per call — the same discipline as the user
/// store — so the store is safe to share across the request threads.
/// </para>
/// </remarks>
public sealed partial class SqliteRefreshTokenStore : IRefreshTokenStore
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS refresh_token (
            token_hash  TEXT PRIMARY KEY,
            username    TEXT NOT NULL,
            family      TEXT NOT NULL,
            issued_utc  TEXT NOT NULL,
            expires_utc TEXT NOT NULL,
            revoked     INTEGER NOT NULL DEFAULT 0);
        CREATE INDEX IF NOT EXISTS ix_refresh_token_family ON refresh_token (family);
        """;

    private readonly string connectionString;
    private readonly ILogger<SqliteRefreshTokenStore> logger;

    /// <summary>Open (creating if absent) the refresh-token store at
    /// <paramref name="dbPath"/> and ensure its schema. A schema/open failure is
    /// logged, not thrown — the node still boots, just without refresh tokens.</summary>
    public SqliteRefreshTokenStore(string dbPath, ILogger<SqliteRefreshTokenStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        this.logger = logger ?? NullLogger<SqliteRefreshTokenStore>.Instance;
        connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        EnsureSchema();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(connectionString);
        conn.Open();
        return conn;
    }

    private void EnsureSchema()
    {
        try
        {
            using var conn = Open();
            conn.Execute("PRAGMA journal_mode=WAL;");
            conn.Execute(SchemaSql);
        }
        catch (SqliteException ex)
        {
            LogSchemaFailed(ex, connectionString);
        }
    }

    /// <inheritdoc/>
    public bool Insert(RefreshTokenRecord token)
    {
        ArgumentNullException.ThrowIfNull(token);
        try
        {
            using var conn = Open();
            conn.Execute(
                "INSERT INTO refresh_token (token_hash, username, family, issued_utc, expires_utc, revoked) " +
                "VALUES (@h, @u, @f, @i, @e, @r);",
                new
                {
                    h = token.TokenHash,
                    u = token.Username,
                    f = token.Family,
                    i = Stamp(token.IssuedUtc),
                    e = Stamp(token.ExpiresUtc),
                    r = token.Revoked ? 1 : 0,
                });
            return true;
        }
        catch (SqliteException ex)
        {
            LogWriteFailed(ex, connectionString);
            return false;
        }
    }

    /// <inheritdoc/>
    public RefreshTokenRecord? FindByHash(string tokenHash)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        try
        {
            using var conn = Open();
            var row = conn.QuerySingleOrDefault<TokenRow>(
                "SELECT token_hash AS TokenHash, username AS Username, family AS Family, " +
                "issued_utc AS IssuedUtc, expires_utc AS ExpiresUtc, revoked AS Revoked " +
                "FROM refresh_token WHERE token_hash = @h;",
                new { h = tokenHash });
            return row is null ? null : ToRecord(row);
        }
        catch (Exception ex) when (ex is SqliteException or FormatException)
        {
            LogReadFailed(ex, connectionString);
            return null;
        }
    }

    /// <inheritdoc/>
    public bool Revoke(string tokenHash)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        try
        {
            using var conn = Open();
            int rows = conn.Execute(
                "UPDATE refresh_token SET revoked = 1 WHERE token_hash = @h;",
                new { h = tokenHash });
            return rows > 0;
        }
        catch (SqliteException ex)
        {
            LogWriteFailed(ex, connectionString);
            return false;
        }
    }

    /// <inheritdoc/>
    public int RevokeFamily(string family)
    {
        ArgumentNullException.ThrowIfNull(family);
        try
        {
            using var conn = Open();
            return conn.Execute(
                "UPDATE refresh_token SET revoked = 1 WHERE family = @f AND revoked = 0;",
                new { f = family });
        }
        catch (SqliteException ex)
        {
            LogWriteFailed(ex, connectionString);
            return 0;
        }
    }

    /// <inheritdoc/>
    public int PruneExpired(DateTimeOffset olderThanUtc)
    {
        try
        {
            using var conn = Open();
            return conn.Execute(
                "DELETE FROM refresh_token WHERE expires_utc < @e;",
                new { e = Stamp(olderThanUtc) });
        }
        catch (SqliteException ex)
        {
            LogWriteFailed(ex, connectionString);
            return 0;
        }
    }

    private static RefreshTokenRecord ToRecord(TokenRow row) => new(
        row.TokenHash,
        row.Username,
        row.Family,
        ParseStamp(row.IssuedUtc),
        ParseStamp(row.ExpiresUtc),
        row.Revoked != 0);

    private static string Stamp(DateTimeOffset value) => value.ToString("o", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseStamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    // Dapper row DTO (mutable so Dapper's setter mapping binds it).
    private sealed class TokenRow
    {
        public string TokenHash { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string IssuedUtc { get; set; } = string.Empty;
        public string ExpiresUtc { get; set; } = string.Empty;
        public long Revoked { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Refresh-token store: could not initialise the schema ({Db}); refresh tokens are unavailable for this run.")]
    private partial void LogSchemaFailed(Exception ex, string db);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Refresh-token store: a read failed ({Db}); treating as absent.")]
    private partial void LogReadFailed(Exception ex, string db);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Refresh-token store: a write failed ({Db}); the change was not persisted.")]
    private partial void LogWriteFailed(Exception ex, string db);
}
