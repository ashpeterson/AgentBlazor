using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AgentBlazor.Core.Paid;

/// <summary>
/// SQLite-backed action history store for Pro/Enterprise tiers.
/// Provides durable persistence across app restarts.
/// </summary>
public sealed class SqliteActionHistoryStore : IActionHistoryStore, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Creates a new SQLite action history store.
    /// </summary>
    /// <param name="connectionString">SQLite connection string. Defaults to local file.</param>
    public SqliteActionHistoryStore(string? connectionString = null)
    {
        connectionString ??= "Data Source=agentblazor-history.db";
        _connection = new SqliteConnection(connectionString);
    }

    /// <summary>
    /// Creates a store with a specific database path.
    /// </summary>
    public static SqliteActionHistoryStore CreateWithPath(string dbPath)
    {
        return new SqliteActionHistoryStore($"Data Source={dbPath}");
    }

    internal static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS action_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                user_id TEXT,
                timestamp TEXT NOT NULL,
                user_message TEXT NOT NULL,
                action_id TEXT NOT NULL,
                agent_id TEXT NOT NULL,
                args_json TEXT NOT NULL,
                succeeded INTEGER NOT NULL DEFAULT 1,
                duration_ms INTEGER,
                route TEXT,
                error_message TEXT,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS idx_session_id ON action_history(session_id);
            CREATE INDEX IF NOT EXISTS idx_user_id ON action_history(user_id);
            CREATE INDEX IF NOT EXISTS idx_timestamp ON action_history(timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_action_id ON action_history(action_id);
            CREATE INDEX IF NOT EXISTS idx_succeeded ON action_history(succeeded);
            """;

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            await _connection.OpenAsync(ct).ConfigureAwait(false);
            await EnsureSchemaAsync(_connection, ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RecordAsync(ActionHistoryEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO action_history (session_id, user_id, timestamp, user_message, action_id, agent_id, args_json, succeeded, duration_ms, route, error_message)
                VALUES (@sessionId, @userId, @timestamp, @userMessage, @actionId, @agentId, @argsJson, @succeeded, @durationMs, @route, @errorMessage)
                """;

            cmd.Parameters.AddWithValue("@sessionId", entry.SessionId);
            cmd.Parameters.AddWithValue("@userId", entry.UserId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@timestamp", entry.Timestamp.ToString("O"));
            cmd.Parameters.AddWithValue("@userMessage", entry.UserMessage);
            cmd.Parameters.AddWithValue("@actionId", entry.ActionId);
            cmd.Parameters.AddWithValue("@agentId", entry.AgentId);
            cmd.Parameters.AddWithValue("@argsJson", JsonSerializer.Serialize(entry.Args, JsonOptions));
            cmd.Parameters.AddWithValue("@succeeded", entry.Succeeded ? 1 : 0);
            cmd.Parameters.AddWithValue("@durationMs", entry.Duration.HasValue ? (object)(long)entry.Duration.Value.TotalMilliseconds : DBNull.Value);
            cmd.Parameters.AddWithValue("@route", entry.Route ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@errorMessage", entry.ErrorMessage ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<ActionHistoryEntry>> GetRecentAsync(
        string sessionId, int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT session_id, user_id, timestamp, user_message, action_id, agent_id, args_json, succeeded, duration_ms, route, error_message
                FROM action_history
                WHERE session_id = @sessionId
                ORDER BY timestamp DESC
                LIMIT @limit
                """;

            cmd.Parameters.AddWithValue("@sessionId", sessionId);
            cmd.Parameters.AddWithValue("@limit", limit);

            var results = new List<ActionHistoryEntry>();
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                results.Add(ReadEntry(reader));
            }

            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<ActionHistoryEntry>> GetByUserAsync(
        string userId, int limit = 200, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT session_id, user_id, timestamp, user_message, action_id, agent_id, args_json, succeeded, duration_ms, route, error_message
                FROM action_history
                WHERE user_id = @userId
                ORDER BY timestamp DESC
                LIMIT @limit
                """;

            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@limit", limit);

            var results = new List<ActionHistoryEntry>();
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                results.Add(ReadEntry(reader));
            }

            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets aggregated action patterns for a user (for adaptive suggestions).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> GetUserActionPatternsAsync(
        string userId, int days = 30, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT action_id, COUNT(*) as count
                FROM action_history
                WHERE user_id = @userId
                  AND timestamp >= @since
                GROUP BY action_id
                ORDER BY count DESC
                LIMIT 20
                """;

            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@since", DateTimeOffset.UtcNow.AddDays(-days).ToString("O"));

            var patterns = new Dictionary<string, int>();
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var actionId = reader.GetString(0);
                var count = reader.GetInt32(1);
                patterns[actionId] = count;
            }

            return patterns;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Prunes old entries to prevent unbounded growth.
    /// </summary>
    public async Task PruneAsync(int maxAgeDays = 90, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                DELETE FROM action_history
                WHERE timestamp < @cutoff
                """;

            cmd.Parameters.AddWithValue("@cutoff", DateTimeOffset.UtcNow.AddDays(-maxAgeDays).ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static ActionHistoryEntry ReadEntry(SqliteDataReader reader)
    {
        var sessionId = reader.GetString(0);
        var userId = reader.IsDBNull(1) ? null : reader.GetString(1);
        var timestamp = DateTimeOffset.Parse(reader.GetString(2));
        var userMessage = reader.GetString(3);
        var actionId = reader.GetString(4);
        var agentId = reader.GetString(5);
        var argsJson = reader.GetString(6);
        var succeeded = reader.GetInt32(7) == 1;
        var durationMs = reader.IsDBNull(8) ? (long?)null : reader.GetInt64(8);
        var route = reader.IsDBNull(9) ? null : reader.GetString(9);
        var errorMessage = reader.IsDBNull(10) ? null : reader.GetString(10);

        var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(argsJson, JsonOptions)
            ?? new Dictionary<string, object?>();

        return new ActionHistoryEntry(
            sessionId,
            userId,
            timestamp,
            userMessage,
            actionId,
            agentId,
            args,
            succeeded,
            durationMs.HasValue ? TimeSpan.FromMilliseconds(durationMs.Value) : null,
            route,
            errorMessage);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
        _lock.Dispose();
    }
}
