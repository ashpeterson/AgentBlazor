using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis.Tests;

public sealed class AnalysisModelFiltersTests
{
    [Theory]
    [InlineData("MapLayerApplier", "Core/Services/MapLayers/MapLayerApplier.cs")]
    [InlineData("AssetLayerService", "Core/Services/Layers/AssetLayerService.cs")]
    [InlineData("GeoChartRenderer", "Core/Rendering/GeoChartRenderer.cs")]
    public void IsDeveloperFacingService_FiltersUiLayerInfrastructure(string serviceName, string filePath)
    {
        var service = new ServiceModel
        {
            TypeName = serviceName,
            FilePath = filePath,
            Methods =
            [
                new ServiceMethodModel
                {
                    Name = "GetLayerDataAsync",
                    ReturnType = "Task<object>",
                    IsPublic = true,
                    IsAsync = true
                }
            ]
        };

        Assert.False(AnalysisModelFilters.IsDeveloperFacingService(service));
    }

    [Theory]
    [InlineData("MapLayerApplier", "ApplyLayerAsync", "Core/Services/MapLayers/MapLayerApplier.cs")]
    [InlineData("GeoChartRenderer", "RenderChartAsync", "Core/Rendering/GeoChartRenderer.cs")]
    public void IsDeveloperFacingAction_FiltersUiLayerInfrastructure(string serviceName, string methodName, string filePath)
    {
        var action = new ActionModel
        {
            Name = methodName,
            SourceService = serviceName,
            MethodName = methodName,
            FilePath = filePath,
            ExposureMode = ActionExposureMode.Suggested,
            Classification = ActionClassification.Workflow,
            Score = 0.9
        };

        Assert.False(AnalysisModelFilters.IsDeveloperFacingAction(action));
    }
}
