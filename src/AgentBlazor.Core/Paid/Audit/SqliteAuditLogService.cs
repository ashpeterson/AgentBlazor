using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AgentBlazor.Core.Paid.Audit;

/// <summary>
/// SQLite-backed audit log service for Pro/Enterprise tiers.
/// Provides durable, queryable audit trail for compliance.
/// </summary>
public sealed class SqliteAuditLogService : IAuditLogService, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SqliteAuditLogService(string? connectionString = null)
    {
        connectionString ??= "Data Source=agentblazor-audit.db";
        _connection = new SqliteConnection(connectionString);
    }

    public static SqliteAuditLogService CreateWithPath(string dbPath)
    {
        return new SqliteAuditLogService($"Data Source={dbPath}");
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            await _connection.OpenAsync(ct).ConfigureAwait(false);

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS audit_log (
                    id TEXT PRIMARY KEY,
                    timestamp TEXT NOT NULL,
                    user_id TEXT NOT NULL,
                    user_email TEXT,
                    event_type TEXT NOT NULL,
                    target_type TEXT NOT NULL,
                    target_id TEXT NOT NULL,
                    description TEXT NOT NULL,
                    ip_address TEXT,
                    metadata_json TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                );

                CREATE INDEX IF NOT EXISTS idx_audit_user_id ON audit_log(user_id);
                CREATE INDEX IF NOT EXISTS idx_audit_timestamp ON audit_log(timestamp DESC);
                CREATE INDEX IF NOT EXISTS idx_audit_event_type ON audit_log(event_type);
                CREATE INDEX IF NOT EXISTS idx_audit_target ON audit_log(target_type, target_id);
                """;

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task LogAsync(AuditEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO audit_log (id, timestamp, user_id, user_email, event_type, target_type, target_id, description, ip_address, metadata_json)
                VALUES (@id, @timestamp, @userId, @userEmail, @eventType, @targetType, @targetId, @description, @ipAddress, @metadataJson)
                """;

            cmd.Parameters.AddWithValue("@id", evt.Id);
            cmd.Parameters.AddWithValue("@timestamp", evt.Timestamp.ToString("O"));
            cmd.Parameters.AddWithValue("@userId", evt.UserId);
            cmd.Parameters.AddWithValue("@userEmail", evt.UserEmail ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@eventType", evt.EventType.ToString());
            cmd.Parameters.AddWithValue("@targetType", evt.TargetType);
            cmd.Parameters.AddWithValue("@targetId", evt.TargetId);
            cmd.Parameters.AddWithValue("@description", evt.Description);
            cmd.Parameters.AddWithValue("@ipAddress", evt.IpAddress ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@metadataJson", evt.Metadata != null
                ? JsonSerializer.Serialize(evt.Metadata, JsonOptions)
                : DBNull.Value);

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task LogActionAsync(
        string userId,
        string? userEmail,
        string actionId,
        string agentId,
        bool succeeded,
        string? errorMessage = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        var eventType = succeeded ? AuditEventType.ActionExecuted : AuditEventType.ActionFailed;
        var description = succeeded
            ? $"Executed action '{actionId}' via agent '{agentId}'"
            : $"Failed to execute action '{actionId}' via agent '{agentId}': {errorMessage}";

        var metadata = new Dictionary<string, object?>
        {
            ["actionId"] = actionId,
            ["agentId"] = agentId,
            ["succeeded"] = succeeded
        };

        if (errorMessage != null)
        {
            metadata["errorMessage"] = errorMessage;
        }

        await LogAsync(new AuditEvent(
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow,
            userId,
            userEmail,
            eventType,
            "action",
            actionId,
            description,
            ipAddress,
            metadata), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var whereClauses = new List<string>();
            using var cmd = _connection.CreateCommand();

            if (query.UserId != null)
            {
                whereClauses.Add("user_id = @userId");
                cmd.Parameters.AddWithValue("@userId", query.UserId);
            }

            if (query.UserEmail != null)
            {
                whereClauses.Add("user_email = @userEmail");
                cmd.Parameters.AddWithValue("@userEmail", query.UserEmail);
            }

            if (query.EventType != null)
            {
                whereClauses.Add("event_type = @eventType");
                cmd.Parameters.AddWithValue("@eventType", query.EventType.Value.ToString());
            }

            if (query.TargetType != null)
            {
                whereClauses.Add("target_type = @targetType");
                cmd.Parameters.AddWithValue("@targetType", query.TargetType);
            }

            if (query.TargetId != null)
            {
                whereClauses.Add("target_id = @targetId");
                cmd.Parameters.AddWithValue("@targetId", query.TargetId);
            }

            if (query.StartDate != null)
            {
                whereClauses.Add("timestamp >= @startDate");
                cmd.Parameters.AddWithValue("@startDate", query.StartDate.Value.ToString("O"));
            }

            if (query.EndDate != null)
            {
                whereClauses.Add("timestamp <= @endDate");
                cmd.Parameters.AddWithValue("@endDate", query.EndDate.Value.ToString("O"));
            }

            var whereClause = whereClauses.Count > 0
                ? "WHERE " + string.Join(" AND ", whereClauses)
                : "";

            cmd.CommandText = $"""
                SELECT id, timestamp, user_id, user_email, event_type, target_type, target_id, description, ip_address, metadata_json
                FROM audit_log
                {whereClause}
                ORDER BY timestamp DESC
                LIMIT @limit OFFSET @offset
                """;

            cmd.Parameters.AddWithValue("@limit", query.Limit);
            cmd.Parameters.AddWithValue("@offset", query.Offset);

            var results = new List<AuditEvent>();
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                results.Add(ReadEvent(reader));
            }

            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<IReadOnlyList<AuditEvent>> GetByUserAsync(string userId, int limit = 100, CancellationToken ct = default)
    {
        return QueryAsync(new AuditQuery { UserId = userId, Limit = limit }, ct);
    }

    public Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int limit = 100, CancellationToken ct = default)
    {
        return QueryAsync(new AuditQuery { Limit = limit }, ct);
    }

    public async Task<Stream> ExportAsync(AuditQuery query, AuditExportFormat format, CancellationToken ct = default)
    {
        var events = await QueryAsync(query with { Limit = 10000 }, ct).ConfigureAwait(false);
        var stream = new MemoryStream();

        if (format == AuditExportFormat.Json)
        {
            await JsonSerializer.SerializeAsync(stream, events, JsonOptions, ct).ConfigureAwait(false);
        }
        else // CSV
        {
            await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync("Id,Timestamp,UserId,UserEmail,EventType,TargetType,TargetId,Description,IpAddress").ConfigureAwait(false);

            foreach (var evt in events)
            {
                var line = $"\"{evt.Id}\",\"{evt.Timestamp:O}\",\"{evt.UserId}\",\"{evt.UserEmail}\",\"{evt.EventType}\",\"{evt.TargetType}\",\"{evt.TargetId}\",\"{EscapeCsv(evt.Description)}\",\"{evt.IpAddress}\"";
                await writer.WriteLineAsync(line).ConfigureAwait(false);
            }

            await writer.FlushAsync(ct).ConfigureAwait(false);
        }

        stream.Position = 0;
        return stream;
    }

    private static string EscapeCsv(string value)
    {
        return value.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", "");
    }

    public async Task PruneAsync(int retentionDays = 365, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                DELETE FROM audit_log
                WHERE timestamp < @cutoff
                """;

            cmd.Parameters.AddWithValue("@cutoff", DateTimeOffset.UtcNow.AddDays(-retentionDays).ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static AuditEvent ReadEvent(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        var timestamp = DateTimeOffset.Parse(reader.GetString(1));
        var userId = reader.GetString(2);
        var userEmail = reader.IsDBNull(3) ? null : reader.GetString(3);
        var eventType = Enum.Parse<AuditEventType>(reader.GetString(4));
        var targetType = reader.GetString(5);
        var targetId = reader.GetString(6);
        var description = reader.GetString(7);
        var ipAddress = reader.IsDBNull(8) ? null : reader.GetString(8);
        var metadataJson = reader.IsDBNull(9) ? null : reader.GetString(9);

        IReadOnlyDictionary<string, object?>? metadata = null;
        if (metadataJson != null)
        {
            metadata = JsonSerializer.Deserialize<Dictionary<string, object?>>(metadataJson, JsonOptions);
        }

        return new AuditEvent(id, timestamp, userId, userEmail, eventType, targetType, targetId, description, ipAddress, metadata);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
        _lock.Dispose();
    }
}
