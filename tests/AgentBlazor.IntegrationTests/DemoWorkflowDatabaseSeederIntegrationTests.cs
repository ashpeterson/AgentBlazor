using AgentBlazor.Demo.Data;
using AgentBlazor.Demo.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.IntegrationTests;

public class DemoWorkflowDatabaseSeederIntegrationTests
{
    [Fact]
    public async Task InitializeAsync_CanRunRepeatedly_WhenDojoWorkspaceColumnsAlreadyExist()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContextFactory<DemoWorkflowDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<DemoWorkflowDatabaseSeeder>();

        await using var provider = services.BuildServiceProvider();
        var seeder = provider.GetRequiredService<DemoWorkflowDatabaseSeeder>();

        await seeder.InitializeAsync(CancellationToken.None);
        await seeder.InitializeAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DemoWorkflowDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var columns = await GetColumnNamesAsync(db, "dojo_workspaces");

        Assert.Contains("BudgetFriendly", columns);
        Assert.Contains("OnePotMeal", columns);
        Assert.Contains("Vegan", columns);
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(DemoWorkflowDbContext db, string tableName)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }
}
