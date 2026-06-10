using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis.Tests;

public sealed class WorkflowClusterAnalyzerTests
{
    [Fact]
    public void Analyze_GroupsRevisionSubmissionLifecycleMethods()
    {
        var analyzer = new WorkflowClusterAnalyzer();
        var model = new ProjectModel
        {
            Services =
            [
                new ServiceModel
                {
                    TypeName = "RevisionSubmissionService",
                    FilePath = "Services/RevisionSubmissionService.cs",
                    Methods =
                    [
                        CreateMethod("CheckStatusAsync"),
                        CreateMethod("GeneratePackageAsync"),
                        CreateMethod("PromoteAsync"),
                        CreateMethod("SubmitAsync"),
                        CreateMethod("UploadPackageAsync")
                    ]
                }
            ],
            Actions =
            [
                CreateAction("RevisionSubmissionService", "CheckStatusAsync", false, 0.7),
                CreateAction("RevisionSubmissionService", "GeneratePackageAsync", true, 0.8),
                CreateAction("RevisionSubmissionService", "PromoteAsync", true, 0.8),
                CreateAction("RevisionSubmissionService", "SubmitAsync", true, 0.9, ["/developers/"]),
                CreateAction("RevisionSubmissionService", "UploadPackageAsync", true, 0.8)
            ]
        };

        var clusters = analyzer.Analyze(model);

        var cluster = Assert.Single(clusters);
        Assert.Equal("Revision Submission Package Pipeline", cluster.Name);
        Assert.Equal("same-service lifecycle", cluster.Origin);
        Assert.True(cluster.RequiresApproval);
        Assert.Equal("approval required", cluster.Risk);
        Assert.Contains("RevisionSubmissionService", cluster.RelatedServices);
        Assert.Contains("Submission", cluster.DomainTerms);
        Assert.Contains("method names form an ordered process sequence", cluster.Evidence);
        Assert.Contains("/developers/", cluster.RouteHints);
        Assert.Equal(
            ["GeneratePackageAsync", "UploadPackageAsync", "SubmitAsync", "CheckStatusAsync", "PromoteAsync"],
            cluster.Methods.Select(method => method.Method).ToArray());
        Assert.Equal(
            ["generate", "upload", "submit", "status", "promote"],
            cluster.Methods.Select(method => method.Role).ToArray());
    }

    [Fact]
    public void Analyze_GroupsCrossServiceRouteAndDomainWorkflow()
    {
        var analyzer = new WorkflowClusterAnalyzer();
        var model = new ProjectModel
        {
            Pages =
            [
                new PageModel
                {
                    Route = "/packages/review",
                    ComponentName = "PackageReview",
                    SuggestedActions =
                    [
                        "package-generation-service-generate-package-async",
                        "package-upload-service-upload-package-async",
                        "package-submission-service-submit-package-async"
                    ]
                }
            ],
            Services =
            [
                CreateService("PackageGenerationService", "GeneratePackageAsync"),
                CreateService("PackageUploadService", "UploadPackageAsync"),
                CreateService("PackageSubmissionService", "SubmitPackageAsync")
            ],
            Actions =
            [
                CreateAction("PackageGenerationService", "GeneratePackageAsync", true, 0.82, ["/packages/review"]),
                CreateAction("PackageUploadService", "UploadPackageAsync", true, 0.81, ["/packages/review"]),
                CreateAction("PackageSubmissionService", "SubmitPackageAsync", true, 0.84, ["/packages/review"])
            ]
        };

        var clusters = analyzer.Analyze(model);

        var cluster = Assert.Single(clusters);
        Assert.Equal("route-correlated workflow", cluster.Origin);
        Assert.Equal("Package Review Pipeline", cluster.Name);
        Assert.Contains("Package", cluster.DomainTerms);
        Assert.Equal(
            ["PackageGenerationService", "PackageSubmissionService", "PackageUploadService"],
            cluster.RelatedServices.ToArray());
        Assert.Contains("/packages/review", cluster.RouteHints);
        Assert.Contains("methods are used or linked from route /packages/review", cluster.Evidence);
        Assert.Equal(
            ["GeneratePackageAsync", "UploadPackageAsync", "SubmitPackageAsync"],
            cluster.Methods.Select(method => method.Method).ToArray());
    }

    [Fact]
    public void Analyze_DoesNotClusterGenericWorkflowEngineMethods()
    {
        var analyzer = new WorkflowClusterAnalyzer();
        var model = new ProjectModel
        {
            Services =
            [
                new ServiceModel
                {
                    TypeName = "WorkflowService",
                    FilePath = "Services/Workflows/WorkflowService.cs",
                    Methods =
                    [
                        CreateMethod("ExecuteWorkflowAsync"),
                        CreateMethod("LoadWorkflow"),
                        CreateMethod("RunWorkflow")
                    ]
                }
            ],
            Actions =
            [
                CreateAction("WorkflowService", "ExecuteWorkflowAsync", true, 0.9),
                CreateAction("WorkflowService", "LoadWorkflow", false, 0.7),
                CreateAction("WorkflowService", "RunWorkflow", true, 0.8)
            ]
        };

        var clusters = analyzer.Analyze(model);

        Assert.Empty(clusters);
    }

    private static ServiceMethodModel CreateMethod(string name) => new()
    {
        Name = name,
        ReturnType = "Task",
        IsPublic = true,
        IsAsync = name.EndsWith("Async", StringComparison.Ordinal)
    };

    private static ServiceModel CreateService(string service, params string[] methods) => new()
    {
        TypeName = service,
        FilePath = $"Services/{service}.cs",
        Methods = methods.Select(CreateMethod).ToList()
    };

    private static ActionModel CreateAction(
        string service,
        string method,
        bool mutation,
        double score,
        IReadOnlyList<string>? routes = null) => new()
    {
        Id = $"{ToKebabCase(service)}-{ToKebabCase(method)}",
        Name = method,
        SourceService = service,
        MethodName = method,
        FilePath = $"Services/{service}.cs",
        ExposureMode = ActionExposureMode.Suggested,
        Classification = mutation ? ActionClassification.Workflow : ActionClassification.Query,
        IsMutationLikely = mutation,
        RequiresApproval = mutation,
        Score = score,
        RelevantRoutes = routes ?? []
    };

    private static string ToKebabCase(string value)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var character in value)
        {
            if (char.IsUpper(character) && current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }

            current.Append(char.ToLowerInvariant(character));
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return string.Join('-', words);
    }
}
