using AgentBlazor.Cli.Analysis;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis.Tests;

public sealed class PageActionLinkerTests : IDisposable
{
    private readonly string _tempDir;

    public PageActionLinkerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"agentblazor-page-linker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AnalyzePageAsync_LinksInjectedInterface_ToImplementationActions()
    {
        var pagePath = Path.Combine(_tempDir, "BrandsPage.razor");
        await File.WriteAllTextAsync(
            pagePath,
            """
            @page "/catalog/brands"
            @inject IBrandService BrandService

            @code {
                private async Task LoadBrands()
                {
                    await BrandService.GetBrandsAsync();
                }
            }
            """);

        var page = new RazorFileAnalysis
        {
            FilePath = pagePath,
            IsPage = true,
            Routes = [new ExtractedRoute { Template = "/catalog/brands" }],
            InjectedServices =
            [
                new InjectedServiceModel
                {
                    TypeName = "IBrandService",
                    FieldName = "BrandService"
                }
            ]
        };

        var service = new ServiceModel
        {
            TypeName = "BrandApiService",
            ServiceTypes = ["IBrandService"],
            Methods =
            [
                new ServiceMethodModel
                {
                    Name = "GetBrandsAsync",
                    ReturnType = "Task<List<BrandDto>>",
                    IsPublic = true,
                    IsAsync = true
                }
            ]
        };

        var action = new ActionModel
        {
            Id = "brand_api.get_brands",
            SourceService = "BrandApiService",
            MethodName = "GetBrandsAsync",
            ExposureMode = ActionExposureMode.Suggested,
            Classification = ActionClassification.Query,
            Score = 0.8
        };

        var linker = new PageActionLinker();

        var result = await linker.AnalyzePageAsync(page, [service], [action]);

        Assert.Contains("BrandApiService", result.LinkedServices);
        Assert.Contains("brand_api.get_brands", result.LinkedActions);
        Assert.Contains("BrandApiService.GetBrandsAsync", result.MethodCallsDetected);
    }
}
