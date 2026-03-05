using AgentBlazor.Demo.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentBlazor.Demo.Services;

internal sealed class DemoWorkflowDatabaseSeeder(IDbContextFactory<DemoWorkflowDbContext> dbContextFactory)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureDojoWorkspaceSchemaAsync(db, cancellationToken);
        await EnsureFileWorkflowSchemaAsync(db, cancellationToken);
    }

    private static async Task EnsureDojoWorkspaceSchemaAsync(
        DemoWorkflowDbContext db,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS dojo_workspaces (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionKey TEXT NOT NULL,
                Title TEXT NOT NULL,
                Minutes INTEGER NOT NULL,
                Difficulty TEXT NOT NULL,
                HighProtein INTEGER NOT NULL,
                LowCarb INTEGER NOT NULL,
                Spicy INTEGER NOT NULL,
                Vegetarian INTEGER NOT NULL,
                LastSavedUtc TEXT NULL,
                CreatedUtc TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
                UpdatedUtc TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP)
            );
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_dojo_workspaces_SessionKey ON dojo_workspaces (SessionKey);",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS dojo_ingredients (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionKey TEXT NOT NULL,
                IngredientId TEXT NOT NULL,
                Name TEXT NOT NULL,
                Amount TEXT NOT NULL,
                Optional INTEGER NOT NULL,
                Notes TEXT NOT NULL,
                SortOrder INTEGER NOT NULL
            );
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_dojo_ingredients_SessionKey ON dojo_ingredients (SessionKey);",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_dojo_ingredients_SessionKey_IngredientId ON dojo_ingredients (SessionKey, IngredientId);",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS dojo_steps (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionKey TEXT NOT NULL,
                StepNumber INTEGER NOT NULL,
                Text TEXT NOT NULL
            );
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_dojo_steps_SessionKey ON dojo_steps (SessionKey);",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_dojo_steps_SessionKey_StepNumber ON dojo_steps (SessionKey, StepNumber);",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS dojo_run_notes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionKey TEXT NOT NULL,
                TimestampUtc TEXT NOT NULL,
                Message TEXT NOT NULL
            );
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_dojo_run_notes_SessionKey ON dojo_run_notes (SessionKey);",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_dojo_run_notes_SessionKey_TimestampUtc ON dojo_run_notes (SessionKey, TimestampUtc);",
            cancellationToken);
    }

    private static async Task EnsureFileWorkflowSchemaAsync(
        DemoWorkflowDbContext db,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS demo_file_workflow_files (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionKey TEXT NOT NULL,
                FileName TEXT NOT NULL,
                UploadMode TEXT NOT NULL,
                StorageToken TEXT NULL,
                AddedUtc TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
                UpdatedUtc TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP)
            );
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_demo_file_workflow_files_SessionKey ON demo_file_workflow_files (SessionKey);",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_demo_file_workflow_files_SessionKey_FileName ON demo_file_workflow_files (SessionKey, FileName);",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS demo_file_workflow_events (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionKey TEXT NOT NULL,
                TimestampUtc TEXT NOT NULL,
                EventType TEXT NOT NULL,
                FileName TEXT NOT NULL,
                Message TEXT NOT NULL
            );
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_demo_file_workflow_events_SessionKey ON demo_file_workflow_events (SessionKey);",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_demo_file_workflow_events_SessionKey_TimestampUtc ON demo_file_workflow_events (SessionKey, TimestampUtc);",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS demo_file_workflow_jobs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionKey TEXT NOT NULL,
                JobId TEXT NOT NULL,
                Operation TEXT NOT NULL,
                FileName TEXT NOT NULL,
                UploadMode TEXT NOT NULL,
                Status TEXT NOT NULL,
                StorageToken TEXT NULL,
                Message TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
                UpdatedUtc TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
                CompletedUtc TEXT NULL
            );
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_demo_file_workflow_jobs_JobId ON demo_file_workflow_jobs (JobId);",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_demo_file_workflow_jobs_SessionKey ON demo_file_workflow_jobs (SessionKey);",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_demo_file_workflow_jobs_SessionKey_UpdatedUtc ON demo_file_workflow_jobs (SessionKey, UpdatedUtc);",
            cancellationToken);
    }
}
