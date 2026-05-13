using AgentBlazor.Demo.Services;

namespace AgentBlazor.IntegrationTests;

public class RuntimeProbeWorkflowIntegrationTests
{
    [Fact]
    public async Task StructuredErrorDateRangeProbe_ReturnsRecoverableInvalidDateRange()
    {
        var capabilities = new RuntimeProbeCapabilities();

        var result = await capabilities.RunStructuredErrorDateRangeProbeAsync(
            new DateOnly(2026, 5, 10),
            new DateOnly(2026, 5, 1));

        Assert.False(result.Succeeded);
        Assert.False(result.RequiresClarification);
        Assert.Contains("end date must be on or after the start date", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("invalid_date_range", result.Outputs["errorCode"]);
        Assert.Equal(new DateOnly(2026, 5, 10), result.Outputs["startDate"]);
        Assert.Equal(new DateOnly(2026, 5, 1), result.Outputs["endDate"]);
        Assert.Contains(result.NextActions, action => action.Contains("Retry with an endDate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StructuredErrorDateRangeProbe_ReturnsSuccessForValidRange()
    {
        var capabilities = new RuntimeProbeCapabilities();

        var result = await capabilities.RunStructuredErrorDateRangeProbeAsync(
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 10));

        Assert.True(result.Succeeded);
        Assert.Equal("STRUCTURED_ERROR_DATE_RANGE_OK", result.Outputs["probe"]);
        Assert.Equal(new DateOnly(2026, 5, 1), result.Outputs["startDate"]);
        Assert.Equal(new DateOnly(2026, 5, 10), result.Outputs["endDate"]);
    }
}
