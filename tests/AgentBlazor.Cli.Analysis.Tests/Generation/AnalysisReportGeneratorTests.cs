using AgentBlazor.Cli.Analysis.Generation;
using AgentBlazor.Cli.Analysis.Models;
using AgentBlazor.Cli.Analysis.WorkflowSuggestions;

namespace AgentBlazor.Cli.Analysis.Tests.Generation;

public sealed class AnalysisReportGeneratorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AnalysisReportGenerator _generator = new();

    public AnalysisReportGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"agentblazor-analysis-report-{Guid.NewGuid():N}");
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
    public async Task GenerateAsync_WritesAnalysisReport_ToSpecifiedPath()
    {
        var outputPath = Path.Combine(_tempDir, ".agentblazor", "analysis.md");
        var model = CreateModel();

        await _generator.GenerateAsync(model, outputPath);

        Assert.True(File.Exists(outputPath));
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("# AgentBlazor Analysis", content);
        Assert.Contains("## Routes And Pages", content);
        Assert.Contains("## Workflow Suggestions", content);
    }

    [Fact]
    public void GenerateMarkdown_IncludesReadiness_WhenProvided()
    {
        var model = CreateModel();
        var readiness = new InstallReadinessReport
        {
            HostProjectName = "TestApp",
            HostProjectPath = "/tmp/TestApp/TestApp.csproj",
            HostShape = new HostShapeAssessment { Title = "Standard Blazor Web App" },
            Checks =
            [
                new InstallReadinessCheck
                {
                    Id = "agentblazor-services",
                    Title = "AgentBlazor service registration",
                    Status = InstallReadinessStatus.Pass,
                    Message = "Found AddAgentBlazor(...)."
                }
            ]
        };

        var content = _generator.GenerateMarkdown(model, readiness);

        Assert.Contains("## Install Readiness", content);
        Assert.Contains("AgentBlazor service registration", content);
    }

    [Fact]
    public void GenerateMarkdown_IncludesValidatedWorkflowSuggestions_WhenProvided()
    {
        var model = CreateModel();
        var suggestions = new WorkflowSuggestionSet
        {
            Model = "test-model",
            Suggestions =
            [
                new WorkflowSuggestion
                {
                    Name = "Order follow-up",
                    Description = "Find open orders and draft a follow-up.",
                    CapabilityClass = "OrderFollowUpCapabilities",
                    Methods =
                    [
                        new WorkflowMethodReference { Service = "OrderService", Method = "FindOrdersAsync" }
                    ],
                    Reasoning = "The app has an orders page and an order lookup service.",
                    Confidence = 0.84
                }
            ],
            Rejected =
            [
                new RejectedWorkflowSuggestion
                {
                    Name = "Invented workflow",
                    Reason = "Referenced unknown methods: MissingService.RunAsync"
                }
            ]
        };

        var content = _generator.GenerateMarkdown(model, workflowSuggestions: suggestions);

        Assert.Contains("Order follow-up", content);
        Assert.Contains("OrderService.FindOrdersAsync", content);
        Assert.Contains("Rejected Suggestions", content);
        Assert.Contains("MissingService.RunAsync", content);
    }

    [Fact]
    public void GenerateMarkdown_FiltersFrameworkServicesAndUiStateActions_FromDeveloperFacingSections()
    {
        var model = CreateModel() with
        {
            Services =
            [
                .. CreateModel().Services,
                new ServiceModel
                {
                    TypeName = "AgentBlazorBuilder",
                    FilePath = "/repo/src/AgentBlazor.Core/Services/AgentBlazorBuilder.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "AddWorkflow",
                            ReturnType = "AgentBlazorBuilder",
                            IsPublic = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "UiStateService",
                    FilePath = "Services/UiStateService.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "SetSelectedNodeAsync",
                            ReturnType = "Task",
                            IsPublic = true
                        }
                    ]
                }
            ],
            Actions =
            [
                .. CreateModel().Actions,
                new ActionModel
                {
                    Name = "Set selected node",
                    SourceService = "UiStateService",
                    MethodName = "SetSelectedNodeAsync",
                    FilePath = "Services/UiStateService.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Command
                }
            ]
        };

        var content = _generator.GenerateMarkdown(model);

        Assert.DoesNotContain("AgentBlazorBuilder", content);
        Assert.DoesNotContain("SetSelectedNodeAsync", content);
        Assert.Contains("OrderService", content);
        Assert.Contains("FindOrdersAsync", content);
    }

    private static ProjectModel CreateModel() => new()
    {
        AppName = "TestApp",
        BlazorHostProject = "TestApp",
        GeneratedAtUtc = new DateTimeOffset(2026, 5, 27, 12, 0, 0, TimeSpan.Zero),
        Projects =
        [
            new ProjectNode
            {
                Name = "TestApp",
                Path = "/tmp/TestApp/TestApp.csproj",
                TargetFramework = "net10.0",
                IsBlazorProject = true,
                IsHostProject = true
            }
        ],
        Routes =
        [
            new RouteModel
            {
                Template = "/orders",
                ComponentName = "Orders",
                ComponentFile = "Components/Pages/Orders.razor"
            }
        ],
        Pages =
        [
            new PageModel
            {
                Route = "/orders",
                ComponentName = "Orders",
                SuggestedActions = ["order_service.find_orders"]
            }
        ],
        Services =
        [
            new ServiceModel
            {
                TypeName = "OrderService",
                FilePath = "Services/OrderService.cs",
                Lifetime = "Scoped",
                Methods =
                [
                    new ServiceMethodModel
                    {
                        Name = "FindOrdersAsync",
                        ReturnType = "Task<IReadOnlyList<Order>>",
                        IsAsync = true,
                        IsPublic = true
                    }
                ]
            }
        ],
        Actions =
        [
            new ActionModel
            {
                Name = "Find Orders",
                SourceService = "OrderService",
                MethodName = "FindOrdersAsync",
                FilePath = "Services/OrderService.cs",
                Classification = ActionClassification.Query,
                Score = 0.92,
                ExposureMode = ActionExposureMode.Suggested,
                Summary = "Useful order lookup action.",
                RelevantRoutes = ["/orders"]
            }
        ]
    };
}
