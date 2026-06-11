using AgentBlazor.Cli.Analysis.Models;
using AgentBlazor.Cli.Analysis.WorkflowSuggestions;

namespace AgentBlazor.Cli.Analysis.Tests.WorkflowSuggestions;

public sealed class WorkflowSuggestionPromptBuilderTests
{
    [Fact]
    public void Build_FiltersFrameworkServicesAndUiStateMethods()
    {
        var builder = new WorkflowSuggestionPromptBuilder();
        var model = new ProjectModel
        {
            AppName = "TestApp",
            BlazorHostProject = "TestApp",
            Services =
            [
                new ServiceModel
                {
                    TypeName = "OrderService",
                    FilePath = "Services/OrderService.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "FindOrdersAsync",
                            ReturnType = "Task<IReadOnlyList<Order>>",
                            IsPublic = true,
                            IsAsync = true
                        },
                        new ServiceMethodModel
                        {
                            Name = "CreateOrderAsync",
                            ReturnType = "Task",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "OrderWorkflowService",
                    FilePath = "Services/OrderWorkflowService.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "SubmitOrderAsync",
                            ReturnType = "Task",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "ActivityIdHelper",
                    FilePath = "ActivityIdHelper.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "ToString",
                            ReturnType = "string",
                            IsPublic = true
                        }
                    ]
                },
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
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "DialogServiceHelper",
                    FilePath = "Services/DialogServiceHelper.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "ShowDialogAsync",
                            ReturnType = "Task",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "AccountController",
                    FilePath = "Controllers/AccountController.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "Register",
                            ReturnType = "Task<ApiResponse>",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "AIChatMessageAssetService",
                    FilePath = "Core/Services/AI/Chat/AIChatMessageAssetService.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "GetLatestChatIdAsync",
                            ReturnType = "Task<string?>",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "AIJobRunnerService",
                    FilePath = "Core/Services/AI/Runner/AIJobRunnerService.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "RunNowAsync",
                            ReturnType = "Task<JobResult>",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "TenantStore",
                    FilePath = "Tenants/TenantStore.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "GetAllAsync",
                            ReturnType = "Task<IEnumerable<TenantInfo>>",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "OrderCapabilities",
                    FilePath = "Workflows/OrderCapabilities.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "ShowOpenOrdersAsync",
                            ReturnType = "Task<CapabilityResult>",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                }
            ],
            Actions =
            [
                new ActionModel
                {
                    Name = "Create Order",
                    SourceService = "OrderService",
                    MethodName = "CreateOrderAsync",
                    FilePath = "Services/OrderService.cs",
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Command,
                    IsMutationLikely = true,
                    Score = 0.9
                },
                new ActionModel
                {
                    Name = "Find Orders",
                    SourceService = "OrderService",
                    MethodName = "FindOrdersAsync",
                    FilePath = "Services/OrderService.cs",
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Query,
                    Score = 0.8
                },
                new ActionModel
                {
                    Name = "Submit Order",
                    SourceService = "OrderWorkflowService",
                    MethodName = "SubmitOrderAsync",
                    FilePath = "Services/OrderWorkflowService.cs",
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Workflow,
                    IsMutationLikely = true,
                    Score = 0.9
                },
                new ActionModel
                {
                    Name = "Get Latest Chat ID",
                    SourceService = "AIChatMessageAssetService",
                    MethodName = "GetLatestChatIdAsync",
                    FilePath = "Core/Services/AI/Chat/AIChatMessageAssetService.cs",
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Query,
                    Score = 0.95
                },
                new ActionModel
                {
                    Name = "Run Now",
                    SourceService = "AIJobRunnerService",
                    MethodName = "RunNowAsync",
                    FilePath = "Core/Services/AI/Runner/AIJobRunnerService.cs",
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Workflow,
                    IsMutationLikely = true,
                    Score = 0.95
                },
                new ActionModel
                {
                    Name = "Get All",
                    SourceService = "TenantStore",
                    MethodName = "GetAllAsync",
                    FilePath = "Tenants/TenantStore.cs",
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Query,
                    Score = 0.95
                },
                new ActionModel
                {
                    Name = "Show Open Orders",
                    SourceService = "OrderCapabilities",
                    MethodName = "ShowOpenOrdersAsync",
                    FilePath = "Workflows/OrderCapabilities.cs",
                    ExposureMode = ActionExposureMode.Confirmed,
                    Classification = ActionClassification.Query
                }
            ]
        };

        var prompt = builder.Build(model);

        Assert.Contains("OrderService", prompt);
        Assert.Contains("FindOrdersAsync", prompt);
        Assert.Contains("approvalRecommended=false", prompt);
        Assert.Contains("CreateOrderAsync", prompt);
        Assert.Contains("approvalRecommended=true", prompt);
        Assert.Contains("risk=safe read-only", prompt);
        Assert.Contains("risk=approval required", prompt);
        Assert.Contains("one-method getter/list/view is usually a data surface", prompt);
        Assert.Contains("Prefer methods classified as Workflow first", prompt);
        Assert.Contains("classification=Workflow", prompt);
        Assert.Contains("classification=Query", prompt);
        Assert.Contains("RequiresApproval = true", prompt);
        Assert.Contains("business outcome", prompt);
        Assert.Contains("Avoid suggesting UI rendering, map layer application, chart drawing, styling, layout, or component state plumbing", prompt);
        Assert.DoesNotContain("ActivityIdHelper", prompt);
        Assert.DoesNotContain("ToString", prompt);
        Assert.DoesNotContain("AgentBlazorBuilder", prompt);
        Assert.DoesNotContain("SetSelectedNodeAsync", prompt);
        Assert.DoesNotContain("DialogServiceHelper", prompt);
        Assert.DoesNotContain("ShowDialogAsync", prompt);
        Assert.DoesNotContain("AccountController", prompt);
        Assert.DoesNotContain("AIChatMessageAssetService", prompt);
        Assert.DoesNotContain("GetLatestChatIdAsync", prompt);
        Assert.DoesNotContain("AIJobRunnerService", prompt);
        Assert.DoesNotContain("RunNowAsync", prompt);
        Assert.DoesNotContain("TenantStore", prompt);
        Assert.Contains("OrderCapabilities.ShowOpenOrdersAsync", prompt);
        Assert.DoesNotContain("- OrderCapabilities [", prompt);
    }

    [Fact]
    public void Build_AllowsDuplicateActionKeysFromSolutionScan()
    {
        var builder = new WorkflowSuggestionPromptBuilder();
        var model = new ProjectModel
        {
            AppName = "TestApp",
            BlazorHostProject = "TestApp",
            Services =
            [
                new ServiceModel
                {
                    TypeName = "EmailService",
                    FilePath = "TenantA/EmailService.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "SendEmailAsync",
                            ReturnType = "Task",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                }
            ],
            Actions =
            [
                new ActionModel
                {
                    Name = "Send Email",
                    SourceService = "EmailService",
                    MethodName = "SendEmailAsync",
                    FilePath = "TenantA/EmailService.cs",
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Command,
                    IsMutationLikely = true,
                    Score = 0.8
                },
                new ActionModel
                {
                    Name = "Send Email",
                    SourceService = "EmailService",
                    MethodName = "SendEmailAsync",
                    FilePath = "TenantB/EmailService.cs",
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Command,
                    IsMutationLikely = true,
                    Score = 0.7
                }
            ]
        };

        var prompt = builder.Build(model);

        Assert.Contains("EmailService", prompt);
        Assert.Contains("SendEmailAsync", prompt);
    }

    [Fact]
    public void Build_IncludesWorkflowClustersBeforeFlatServiceCatalog()
    {
        var builder = new WorkflowSuggestionPromptBuilder();
        var model = new ProjectModel
        {
            AppName = "TestApp",
            BlazorHostProject = "TestApp",
            WorkflowClusters =
            [
                new WorkflowClusterModel
                {
                    Name = "Revision Submission Package Pipeline",
                    SourceService = "RevisionSubmissionService",
                    Risk = "approval required",
                    Origin = "same-service lifecycle",
                    RequiresApproval = true,
                    Confidence = 0.88,
                    Summary = "Revision Submission Package Pipeline appears to be a multi-step process: GeneratePackageAsync -> UploadPackageAsync -> SubmitAsync -> CheckStatusAsync -> PromoteAsync.",
                    DomainTerms = ["Revision", "Submission", "Package"],
                    RelatedServices = ["RevisionSubmissionService"],
                    Evidence = ["same service contains 5 lifecycle methods", "method names form an ordered process sequence"],
                    RouteHints = ["/developers/"],
                    Methods =
                    [
                        CreateClusterMethod("GeneratePackageAsync", "generate"),
                        CreateClusterMethod("UploadPackageAsync", "upload"),
                        CreateClusterMethod("SubmitAsync", "submit"),
                        CreateClusterMethod("CheckStatusAsync", "status"),
                        CreateClusterMethod("PromoteAsync", "promote")
                    ]
                },
                new WorkflowClusterModel
                {
                    Name = "Password Reset Token Pipeline",
                    SourceService = "UserService",
                    Risk = "high-risk/admin",
                    Origin = "same-service lifecycle",
                    RequiresApproval = true,
                    Confidence = 0.91,
                    Summary = "Password Reset Token Pipeline appears to be a sensitive account recovery process.",
                    DomainTerms = ["Password", "Reset", "Token"],
                    RelatedServices = ["UserService"],
                    Methods =
                    [
                        CreateClusterMethod("UserService", "GenerateResetTokenAsync", "generate"),
                        CreateClusterMethod("UserService", "ValidateResetTokenAsync", "validate"),
                        CreateClusterMethod("UserService", "SendPasswordResetEmailAsync", "submit")
                    ]
                }
            ],
            Services =
            [
                new ServiceModel
                {
                    TypeName = "RevisionSubmissionService",
                    FilePath = "Services/RevisionSubmissionService.cs",
                    Methods =
                    [
                        new ServiceMethodModel { Name = "GeneratePackageAsync", IsPublic = true, IsAsync = true },
                        new ServiceMethodModel { Name = "UploadPackageAsync", IsPublic = true, IsAsync = true },
                        new ServiceMethodModel { Name = "SubmitAsync", IsPublic = true, IsAsync = true },
                        new ServiceMethodModel { Name = "CheckStatusAsync", IsPublic = true, IsAsync = true },
                        new ServiceMethodModel { Name = "PromoteAsync", IsPublic = true, IsAsync = true }
                    ]
                }
            ],
            Actions =
            [
                CreateAction("GeneratePackageAsync"),
                CreateAction("UploadPackageAsync"),
                CreateAction("SubmitAsync"),
                CreateAction("CheckStatusAsync"),
                CreateAction("PromoteAsync")
            ]
        };

        var prompt = builder.Build(model);

        Assert.Contains("Prioritize workflow clusters when present", prompt);
        Assert.Contains("Preferred workflow clusters:", prompt);
        Assert.Contains("Revision Submission Package Pipeline", prompt);
        Assert.Contains("origin=same-service lifecycle", prompt);
        Assert.Contains("domainTerms=Revision, Submission, Package", prompt);
        Assert.Contains("evidence=same service contains 5 lifecycle methods", prompt);
        Assert.Contains("routeHints=/developers/", prompt);
        Assert.Contains("RevisionSubmissionService.GeneratePackageAsync [role=generate", prompt);
        Assert.Contains("RevisionSubmissionService.PromoteAsync [role=promote", prompt);
        Assert.Contains("Sensitive/supporting workflow clusters:", prompt);
        Assert.Contains("Do not suggest these before preferred clusters", prompt);
        Assert.Contains("Password Reset Token Pipeline: origin=same-service lifecycle, risk=high-risk/admin", prompt);
        Assert.DoesNotContain("UserService.GenerateResetTokenAsync [role=generate", prompt);
        Assert.True(prompt.IndexOf("Preferred workflow clusters:", StringComparison.Ordinal) <
            prompt.IndexOf("Discovered services and public methods:", StringComparison.Ordinal));
        Assert.Contains("Use this flat catalog as supporting context", prompt);
    }

    private static WorkflowClusterMethodModel CreateClusterMethod(string method, string role)
        => CreateClusterMethod("RevisionSubmissionService", method, role);

    private static WorkflowClusterMethodModel CreateClusterMethod(string service, string method, string role) => new()
    {
        Service = service,
        Method = method,
        Role = role,
        Classification = ActionClassification.Workflow,
        Risk = "approval required"
    };

    private static ActionModel CreateAction(string method) => new()
    {
        Name = method,
        SourceService = "RevisionSubmissionService",
        MethodName = method,
        FilePath = "Services/RevisionSubmissionService.cs",
        ExposureMode = ActionExposureMode.Suggested,
        Classification = ActionClassification.Workflow,
        IsMutationLikely = true,
        RequiresApproval = true,
        Score = 0.8
    };
}
