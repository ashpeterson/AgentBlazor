using AgentBlazor.Core.Runtime.Preferences;

namespace AgentBlazor.Core.Tests;

public class UserPreferenceServiceTests
{
    [Fact]
    public async Task RecordActionAsync_TracksActionFrequency()
    {
        var service = new InMemoryUserPreferenceService();

        await service.RecordActionAsync("session-1", "AgentDataGrid", "sort", null);
        await service.RecordActionAsync("session-1", "AgentDataGrid", "sort", null);
        await service.RecordActionAsync("session-1", "AgentDataGrid", "sort", null);

        var preferences = await service.GetPreferencesAsync("session-1");

        Assert.True(preferences.ActionFrequency.TryGetValue("AgentDataGrid.sort", out var count));
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task RecordActionAsync_TracksLastUsedValues()
    {
        var service = new InMemoryUserPreferenceService();
        var arguments = new Dictionary<string, object?>
        {
            ["column"] = "RiskScore",
            ["direction"] = "desc"
        };

        await service.RecordActionAsync("session-1", "AgentDataGrid", "sort", arguments);

        var preferences = await service.GetPreferencesAsync("session-1");

        Assert.True(preferences.LastUsedValues.TryGetValue("column", out var column));
        Assert.Equal("RiskScore", column);
        Assert.True(preferences.LastUsedValues.TryGetValue("direction", out var direction));
        Assert.Equal("desc", direction);
    }

    [Fact]
    public async Task GetPreferencesAsync_ReturnsEmptyForNewSession()
    {
        var service = new InMemoryUserPreferenceService();

        var preferences = await service.GetPreferencesAsync("new-session");

        Assert.NotNull(preferences);
        Assert.Empty(preferences.ActionFrequency);
        Assert.Empty(preferences.LastUsedValues);
        Assert.Empty(preferences.FrequentRoutes);
    }

    [Fact]
    public async Task RecordActionAsync_TracksMultipleActions()
    {
        var service = new InMemoryUserPreferenceService();

        await service.RecordActionAsync("session-1", "AgentDataGrid", "sort", null);
        await service.RecordActionAsync("session-1", "AgentDataGrid", "filter", null);
        await service.RecordActionAsync("session-1", "AgentNavMenu", "navigate_to", null);

        var preferences = await service.GetPreferencesAsync("session-1");

        Assert.Equal(3, preferences.ActionFrequency.Count);
    }

    [Fact]
    public async Task MultipleSessions_AreIsolated()
    {
        var service = new InMemoryUserPreferenceService();

        await service.RecordActionAsync("session-1", "AgentDataGrid", "sort", null);
        await service.RecordActionAsync("session-2", "AgentNavMenu", "navigate_to", null);

        var prefs1 = await service.GetPreferencesAsync("session-1");
        var prefs2 = await service.GetPreferencesAsync("session-2");

        Assert.True(prefs1.ActionFrequency.ContainsKey("AgentDataGrid.sort"));
        Assert.False(prefs1.ActionFrequency.ContainsKey("AgentNavMenu.navigate_to"));

        Assert.True(prefs2.ActionFrequency.ContainsKey("AgentNavMenu.navigate_to"));
        Assert.False(prefs2.ActionFrequency.ContainsKey("AgentDataGrid.sort"));
    }

    [Fact]
    public async Task RecordActionAsync_NavigateTo_TracksFrequentRoutes()
    {
        var service = new InMemoryUserPreferenceService();
        var arguments = new Dictionary<string, object?> { ["uri"] = "/suppliers" };

        await service.RecordActionAsync("session-1", "AgentNavMenu", "navigate_to", arguments);

        var preferences = await service.GetPreferencesAsync("session-1");

        Assert.Contains("/suppliers", preferences.FrequentRoutes);
    }

    [Fact]
    public async Task RecordActionAsync_WithNullArguments_DoesNotThrow()
    {
        var service = new InMemoryUserPreferenceService();

        var exception = await Record.ExceptionAsync(async () =>
            await service.RecordActionAsync("session-1", "Component", "action", null));

        Assert.Null(exception);
    }

    [Fact]
    public void UserPreferences_Empty_CreatesEmptyCollections()
    {
        var preferences = UserPreferences.Empty;

        Assert.NotNull(preferences);
        Assert.Empty(preferences.ActionFrequency);
        Assert.Empty(preferences.LastUsedValues);
        Assert.Empty(preferences.FrequentRoutes);
    }

    [Fact]
    public void ActionRecord_StoresAllProperties()
    {
        var timestamp = DateTime.UtcNow;
        var arguments = new Dictionary<string, object?> { ["key"] = "value" };

        var record = new ActionRecord
        {
            ComponentId = "TestComponent",
            ActionId = "testAction",
            Arguments = arguments,
            Timestamp = timestamp
        };

        Assert.Equal("TestComponent", record.ComponentId);
        Assert.Equal("testAction", record.ActionId);
        Assert.Same(arguments, record.Arguments);
        Assert.Equal(timestamp, record.Timestamp);
    }
}
