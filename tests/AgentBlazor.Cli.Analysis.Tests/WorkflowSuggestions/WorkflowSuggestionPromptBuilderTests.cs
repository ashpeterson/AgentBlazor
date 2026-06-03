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
        Assert.Contains("Prefer safe read-only workflows first", prompt);
        Assert.Contains("RequiresApproval = true", prompt);
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
}
