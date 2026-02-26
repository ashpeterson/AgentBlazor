using AgentBlazor.Core.Components;

namespace AgentBlazor.Demo.Services;

internal sealed class DemoChartDataSources(SupplierWorkflowService workflowService)
{
    public async ValueTask<AgentChartDataResult?> ResolveAsync(
        AgentChartDataRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DataSource))
        {
            return null;
        }

        var source = request.DataSource.Trim().ToLowerInvariant();
        return source switch
        {
            "demo.suppliers.risk.by-region" => await ResolveRiskByRegionAsync(cancellationToken),
            "demo.suppliers.risk.tier-distribution" => await ResolveRiskTierDistributionAsync(cancellationToken),
            "demo.onboarding.volume.monthly" => await ResolveMonthlyOnboardingAsync(request.Arguments, cancellationToken),
            _ => null
        };
    }

    private async Task<AgentChartDataResult> ResolveRiskByRegionAsync(CancellationToken cancellationToken)
    {
        var points = await workflowService.GetAverageRiskByRegionAsync(cancellationToken);
        return new AgentChartDataResult
        {
            Title = "Average Supplier Risk by Region",
            Description = "Calculated from the currently persisted supplier records.",
            ChartType = AgentUiChartType.Bar,
            Labels = points.Select(static x => x.Region).ToArray(),
            Series =
            [
                new AgentUiChartSeries
                {
                    Name = "Average Risk",
                    Data = points.Select(static x => x.AverageRisk).ToArray()
                }
            ]
        };
    }

    private async Task<AgentChartDataResult> ResolveRiskTierDistributionAsync(CancellationToken cancellationToken)
    {
        var points = await workflowService.GetRiskTierDistributionAsync(cancellationToken);
        return new AgentChartDataResult
        {
            Title = "Supplier Risk-Tier Distribution",
            Description = "Count of suppliers per computed risk tier.",
            ChartType = AgentUiChartType.Pie,
            Labels = points.Select(static x => x.Tier).ToArray(),
            Series =
            [
                new AgentUiChartSeries
                {
                    Name = "Supplier Count",
                    Data = points.Select(static x => (double)x.Count).ToArray()
                }
            ]
        };
    }

    private async Task<AgentChartDataResult> ResolveMonthlyOnboardingAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var months = TryGetInt(arguments, "months") ?? 8;
        var points = await workflowService.GetMonthlyOnboardingVolumeAsync(months, cancellationToken);

        return new AgentChartDataResult
        {
            Title = "Onboarding Intake Volume",
            Description = "Monthly count of onboarding requests submitted.",
            ChartType = AgentUiChartType.Line,
            Labels = points.Select(static x => x.Month).ToArray(),
            Series =
            [
                new AgentUiChartSeries
                {
                    Name = "Requests",
                    Data = points.Select(static x => (double)x.Count).ToArray()
                }
            ]
        };
    }

    private static int? TryGetInt(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int number => number,
            long number => checked((int)number),
            double number => (int)number,
            float number => (int)number,
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }
}
