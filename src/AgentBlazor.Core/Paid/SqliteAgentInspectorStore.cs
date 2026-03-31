using System.Text.Json;
using AgentBlazor.Execution;
using Microsoft.Data.Sqlite;

namespace AgentBlazor.Core.Paid;

/// <summary>
/// SQLite-backed inspector store for Pro/Enterprise tiers.
/// Provides durable persistence of inspector runs for debugging and analysis.
/// </summary>
public sealed class SqliteAgentInspectorStore : IAgentInspectorStore, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Creates a new SQLite inspector store.
    /// </summary>
    /// <param name="connectionString">SQLite connection string. Defaults to local file.</param>
    public SqliteAgentInspectorStore(string? connectionString = null)
    {
        connectionString ??= "Data Source=agentblazor-inspector.db";
        _connection = new SqliteConnection(connectionString);
    }

    /// <summary>
    /// Creates a store with a specific database path.
    /// </summary>
    public static SqliteAgentInspectorStore CreateWithPath(string dbPath)
    {
        return new SqliteAgentInspectorStore($"Data Source={dbPath}");
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        _lock.Wait();
        try
        {
            if (_initialized) return;

            _connection.Open();

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS inspector_runs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    run_id TEXT NOT NULL UNIQUE,
                    session_id TEXT NOT NULL,
                    agent_name TEXT NOT NULL,
                    started_at TEXT NOT NULL,
                    finished_at TEXT,
                    system_prompt TEXT,
                    raw_plan_response TEXT,
                    events_json TEXT NOT NULL,
                    succeeded INTEGER NOT NULL,
                    error_message TEXT,
                    execution_plan_json TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                );

                CREATE INDEX IF NOT EXISTS idx_inspector_session_id ON inspector_runs(session_id);
                CREATE INDEX IF NOT EXISTS idx_inspector_started_at ON inspector_runs(started_at DESC);
                """;

            cmd.ExecuteNonQuery();
            _initialized = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void RecordRun(InspectorRunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);

        EnsureInitialized();

        _lock.Wait();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO inspector_runs
                (run_id, session_id, agent_name, started_at, finished_at, system_prompt,
                 raw_plan_response, events_json, succeeded, error_message, execution_plan_json)
                VALUES
                (@runId, @sessionId, @agentName, @startedAt, @finishedAt, @systemPrompt,
                 @rawPlanResponse, @eventsJson, @succeeded, @errorMessage, @executionPlanJson)
                """;

            cmd.Parameters.AddWithValue("@runId", run.RunId);
            cmd.Parameters.AddWithValue("@sessionId", run.SessionId);
            cmd.Parameters.AddWithValue("@agentName", run.AgentName);
            cmd.Parameters.AddWithValue("@startedAt", run.StartedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@finishedAt", run.FinishedAt?.ToString("O") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@systemPrompt", run.SystemPrompt ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@rawPlanResponse", run.RawPlanResponse ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@eventsJson", JsonSerializer.Serialize(run.Events, JsonOptions));
            cmd.Parameters.AddWithValue("@succeeded", run.Succeeded ? 1 : 0);
            cmd.Parameters.AddWithValue("@errorMessage", run.ErrorMessage ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@executionPlanJson",
                run.ExecutionPlan != null ? JsonSerializer.Serialize(run.ExecutionPlan, JsonOptions) : (object)DBNull.Value);

            cmd.ExecuteNonQuery();
        }
        finally
        {
            _lock.Release();
        }
    }

    public IReadOnlyList<InspectorRunRecord> GetRecentRuns(string sessionId, int limit = 20)
    {
        EnsureInitialized();

        _lock.Wait();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT run_id, session_id, agent_name, started_at, finished_at, system_prompt,
                       raw_plan_response, events_json, succeeded, error_message, execution_plan_json
                FROM inspector_runs
                WHERE session_id = @sessionId
                ORDER BY started_at DESC
                LIMIT @limit
                """;

            cmd.Parameters.AddWithValue("@sessionId", sessionId);
            cmd.Parameters.AddWithValue("@limit", limit);

            var results = new List<InspectorRunRecord>();
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                results.Add(ReadRecord(reader));
            }

            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets all runs across sessions (for admin/analytics).
    /// </summary>
    public IReadOnlyList<InspectorRunRecord> GetAllRecentRuns(int limit = 100)
    {
        EnsureInitialized();

        _lock.Wait();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT run_id, session_id, agent_name, started_at, finished_at, system_prompt,
                       raw_plan_response, events_json, succeeded, error_message, execution_plan_json
                FROM inspector_runs
                ORDER BY started_at DESC
                LIMIT @limit
                """;

            cmd.Parameters.AddWithValue("@limit", limit);

            var results = new List<InspectorRunRecord>();
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                results.Add(ReadRecord(reader));
            }

            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Prunes old runs to prevent unbounded growth.
    /// </summary>
    public void Prune(int maxAgeDays = 30)
    {
        EnsureInitialized();

        _lock.Wait();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                DELETE FROM inspector_runs
                WHERE started_at < @cutoff
                """;

            cmd.Parameters.AddWithValue("@cutoff", DateTimeOffset.UtcNow.AddDays(-maxAgeDays).ToString("O"));
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _lock.Release();
        }
    }

    private static InspectorRunRecord ReadRecord(SqliteDataReader reader)
    {
        var runId = reader.GetString(0);
        var sessionId = reader.GetString(1);
        var agentName = reader.GetString(2);
        var startedAt = DateTimeOffset.Parse(reader.GetString(3));
        var finishedAt = reader.IsDBNull(4) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(4));
        var systemPrompt = reader.IsDBNull(5) ? null : reader.GetString(5);
        var rawPlanResponse = reader.IsDBNull(6) ? null : reader.GetString(6);
        var eventsJson = reader.GetString(7);
        var succeeded = reader.GetInt32(8) == 1;
        var errorMessage = reader.IsDBNull(9) ? null : reader.GetString(9);
        var executionPlanJson = reader.IsDBNull(10) ? null : reader.GetString(10);

        var events = JsonSerializer.Deserialize<List<InspectorEvent>>(eventsJson, JsonOptions)
            ?? [];

        AgentExecutionPlan? executionPlan = null;
        if (!string.IsNullOrEmpty(executionPlanJson))
        {
            executionPlan = JsonSerializer.Deserialize<AgentExecutionPlan>(executionPlanJson, JsonOptions);
        }

        return new InspectorRunRecord(
            runId,
            sessionId,
            agentName,
            startedAt,
            finishedAt,
            systemPrompt,
            rawPlanResponse,
            events,
            succeeded,
            errorMessage,
            executionPlan);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
        _lock.Dispose();
    }
}
