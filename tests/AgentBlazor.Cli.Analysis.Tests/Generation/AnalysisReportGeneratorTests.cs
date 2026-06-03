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
        var model = CreateModel() with
        {
            Actions =
            [
                .. CreateModel().Actions,
                new ActionModel
                {
                    Name = "Draft order follow-up",
                    SourceService = "OrderService",
                    MethodName = "DraftFollowUpAsync",
                    FilePath = "Services/OrderService.cs",
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Command,
                    IsMutationLikely = true,
                    Score = 0.88
                }
            ]
        };
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
                        new WorkflowMethodReference { Service = "OrderService", Method = "FindOrdersAsync" },
                        new WorkflowMethodReference { Service = "OrderService", Method = "DraftFollowUpAsync" }
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
        Assert.Contains("RequiresApproval = true", content);
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
                            IsPublic = true
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
                    TypeName = "HubClient",
                    FilePath = "Hubs/HubClient.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "StartAsync",
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
                    TypeName = "EmailFactory",
                    FilePath = "Factories/EmailFactory.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "BuildTestEmail",
                            ReturnType = "EmailMessageDto",
                            IsPublic = true
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
                },
                new ActionModel
                {
                    Name = "Show Dialog",
                    SourceService = "DialogServiceHelper",
                    MethodName = "ShowDialogAsync",
                    FilePath = "Services/DialogServiceHelper.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Query
                },
                new ActionModel
                {
                    Name = "Register",
                    SourceService = "AccountController",
                    MethodName = "Register",
                    FilePath = "Controllers/AccountController.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Workflow
                },
                new ActionModel
                {
                    Name = "Get Latest Chat ID",
                    SourceService = "AIChatMessageAssetService",
                    MethodName = "GetLatestChatIdAsync",
                    FilePath = "Core/Services/AI/Chat/AIChatMessageAssetService.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Query
                },
                new ActionModel
                {
                    Name = "Run Now",
                    SourceService = "AIJobRunnerService",
                    MethodName = "RunNowAsync",
                    FilePath = "Core/Services/AI/Runner/AIJobRunnerService.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Workflow,
                    IsMutationLikely = true
                },
                new ActionModel
                {
                    Name = "Get All",
                    SourceService = "TenantStore",
                    MethodName = "GetAllAsync",
                    FilePath = "Tenants/TenantStore.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Query
                }
            ]
        };

        var content = _generator.GenerateMarkdown(model);

        Assert.DoesNotContain("ActivityIdHelper", content);
        Assert.DoesNotContain("ToString", content);
        Assert.DoesNotContain("AgentBlazorBuilder", content);
        Assert.DoesNotContain("SetSelectedNodeAsync", content);
        Assert.DoesNotContain("DialogServiceHelper", content);
        Assert.DoesNotContain("ShowDialogAsync", content);
        Assert.DoesNotContain("HubClient", content);
        Assert.DoesNotContain("AccountController", content);
        Assert.DoesNotContain("EmailFactory", content);
        Assert.DoesNotContain("AIChatMessageAssetService", content);
        Assert.DoesNotContain("GetLatestChatIdAsync", content);
        Assert.DoesNotContain("AIJobRunnerService", content);
        Assert.DoesNotContain("RunNowAsync", content);
        Assert.DoesNotContain("TenantStore", content);
        Assert.Contains("OrderService", content);
        Assert.Contains("FindOrdersAsync", content);
    }

    [Fact]
    public void GenerateMarkdown_ReportsActionAdoption_FromDeveloperFacingActions()
    {
        var content = _generator.GenerateMarkdown(CreateModel());

        Assert.Contains("AgentBlazor action adoption: 0 confirmed, 1 candidate actions not yet exposed", content);
        Assert.DoesNotContain("Action coverage:", content);
    }

    [Fact]
    public void GenerateMarkdown_DoesNotRepeatCapabilityClasses_AsServices()
    {
        var model = CreateModel() with
        {
            Services =
            [
                .. CreateModel().Services,
                new ServiceModel
                {
                    TypeName = "OrderCapabilities",
                    FilePath = "Workflows/OrderCapabilities.cs",
                    Lifetime = "Scoped",
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
                .. CreateModel().Actions,
                new ActionModel
                {
                    Name = "Show Open Orders",
                    SourceService = "OrderCapabilities",
                    MethodName = "ShowOpenOrdersAsync",
                    FilePath = "Workflows/OrderCapabilities.cs",
                    Classification = ActionClassification.Query,
                    Score = 1,
                    ExposureMode = ActionExposureMode.Confirmed
                }
            ]
        };

        var content = _generator.GenerateMarkdown(model);

        Assert.Contains("OrderCapabilities", content);
        Assert.Contains("ShowOpenOrdersAsync", content);
        Assert.DoesNotContain("### `OrderCapabilities`", content);
    }

    [Fact]
    public void GenerateMarkdown_DoesNotRecommendAgentAction_WhenMethodNameAlreadyHasConfirmedAction()
    {
        var model = CreateModel() with
        {
            Actions =
            [
                .. CreateModel().Actions,
                new ActionModel
                {
                    Name = "Show Open Tickets",
                    SourceService = "SupportCapabilities",
                    MethodName = "ShowOpenTicketsAsync",
                    FilePath = "Workflows/SupportCapabilities.cs",
                    Classification = ActionClassification.Query,
                    Score = 1,
                    ExposureMode = ActionExposureMode.Confirmed
                },
                new ActionModel
                {
                    Name = "Prepare Restock Plan",
                    SourceService = "InventoryService",
                    MethodName = "PrepareRestockPlanAsync",
                    FilePath = "Services/InventoryService.cs",
                    Classification = ActionClassification.Workflow,
                    Score = 0.9,
                    ExposureMode = ActionExposureMode.Suggested
                }
            ],
            Recommendations =
            [
                new RecommendationModel
                {
                    Type = RecommendationType.AddAgentAction,
                    TargetName = "SupportTicketService.ShowOpenTicketsAsync",
                    Suggestion = "Add `[AgentAction(\"Show Open Tickets\")]`",
                    Priority = 1
                },
                new RecommendationModel
                {
                    Type = RecommendationType.AddAgentAction,
                    TargetName = "InventoryService.PrepareRestockPlanAsync",
                    Suggestion = "Add `[AgentAction(\"Prepare Restock Plan\")]`",
                    Priority = 0.9
                }
            ]
        };

        var content = _generator.GenerateMarkdown(model);

        Assert.DoesNotContain("Show Open Tickets", content);
        Assert.Contains("Prepare Restock Plan", content);
    }

    [Fact]
    public void GenerateMarkdown_DoesNotRecommendFilteredInfrastructureActions()
    {
        var model = CreateModel() with
        {
            Actions =
            [
                new ActionModel
                {
                    Name = "Show Dialog",
                    SourceService = "DialogServiceHelper",
                    MethodName = "ShowDialogAsync",
                    FilePath = "Services/DialogServiceHelper.cs",
                    Classification = ActionClassification.Query,
                    Score = 0.95,
                    ExposureMode = ActionExposureMode.Suggested
                },
                new ActionModel
                {
                    Name = "Prepare Restock Plan",
                    SourceService = "InventoryService",
                    MethodName = "PrepareRestockPlanAsync",
                    FilePath = "Services/InventoryService.cs",
                    Classification = ActionClassification.Workflow,
                    Score = 0.9,
                    ExposureMode = ActionExposureMode.Suggested
                }
            ],
            Recommendations =
            [
                new RecommendationModel
                {
                    Type = RecommendationType.AddAgentAction,
                    TargetName = "DialogServiceHelper.ShowDialogAsync",
                    Suggestion = "Add `[AgentAction(\"Show Dialog\")]`",
                    Priority = 1
                },
                new RecommendationModel
                {
                    Type = RecommendationType.AddAgentAction,
                    TargetName = "InventoryService.PrepareRestockPlanAsync",
                    Suggestion = "Add `[AgentAction(\"Prepare Restock Plan\")]`",
                    Priority = 0.9
                }
            ]
        };

        var content = _generator.GenerateMarkdown(model);

        Assert.DoesNotContain("Show Dialog", content);
        Assert.Contains("Prepare Restock Plan", content);
    }

    [Fact]
    public void GenerateMarkdown_DisambiguatesDuplicateStaticCandidateHeadings()
    {
        var model = CreateModel() with
        {
            Actions =
            [
                new ActionModel
                {
                    Name = "Export",
                    SourceService = "ExcelService",
                    MethodName = "ExportAsync",
                    FilePath = "Services/ExcelService.cs",
                    Classification = ActionClassification.Export,
                    Score = 0.95,
                    ExposureMode = ActionExposureMode.Suggested
                },
                new ActionModel
                {
                    Name = "Export",
                    SourceService = "PDFService",
                    MethodName = "ExportAsync",
                    FilePath = "Services/PDFService.cs",
                    Classification = ActionClassification.Export,
                    Score = 0.9,
                    ExposureMode = ActionExposureMode.Suggested
                }
            ]
        };

        var content = _generator.GenerateMarkdown(model);

        Assert.Contains("### Export (ExcelService)", content);
        Assert.Contains("### Export (PDFService)", content);
    }

    [Fact]
    public void GenerateMarkdown_PrioritizesSafeStaticCandidates_BeforeHighRiskAdminActions()
    {
        var model = CreateModel() with
        {
            Actions =
            [
                new ActionModel
                {
                    Name = "Reset Password",
                    SourceService = "UserService",
                    MethodName = "ResetPasswordAsync",
                    FilePath = "Services/UserService.cs",
                    Classification = ActionClassification.Workflow,
                    IsMutationLikely = true,
                    RequiresApproval = true,
                    Score = 1,
                    ExposureMode = ActionExposureMode.Suggested
                },
                new ActionModel
                {
                    Name = "Get Status Snapshot",
                    SourceService = "OriginAIClient",
                    MethodName = "GetStatusSnapshotAsync",
                    FilePath = "Services/OriginAIClient.cs",
                    Classification = ActionClassification.Query,
                    Score = 0.75,
                    ExposureMode = ActionExposureMode.Suggested
                }
            ],
            Recommendations = []
        };

        var content = _generator.GenerateMarkdown(model);

        Assert.True(content.IndexOf("### Get Status Snapshot", StringComparison.Ordinal) <
            content.IndexOf("### Reset Password", StringComparison.Ordinal));
        Assert.Contains("- Risk: safe read-only", content);
        Assert.Contains("- Risk: high-risk/admin", content);
    }

    [Fact]
    public void GenerateMarkdown_SortsLlmSuggestions_ByRiskBeforeConfidence()
    {
        var model = CreateModel() with
        {
            Actions =
            [
                new ActionModel
                {
                    Name = "Reset Password",
                    SourceService = "UserService",
                    MethodName = "ResetPasswordAsync",
                    FilePath = "Services/UserService.cs",
                    Classification = ActionClassification.Workflow,
                    IsMutationLikely = true,
                    RequiresApproval = true,
                    Score = 1,
                    ExposureMode = ActionExposureMode.Suggested
                },
                new ActionModel
                {
                    Name = "Get Status Snapshot",
                    SourceService = "OriginAIClient",
                    MethodName = "GetStatusSnapshotAsync",
                    FilePath = "Services/OriginAIClient.cs",
                    Classification = ActionClassification.Query,
                    Score = 0.75,
                    ExposureMode = ActionExposureMode.Suggested
                }
            ]
        };
        var suggestions = new WorkflowSuggestionSet
        {
            Model = "test-model",
            Suggestions =
            [
                new WorkflowSuggestion
                {
                    Name = "Password reset",
                    Description = "Reset user passwords.",
                    Confidence = 0.95,
                    Methods =
                    [
                        new WorkflowMethodReference { Service = "UserService", Method = "ResetPasswordAsync" }
                    ]
                },
                new WorkflowSuggestion
                {
                    Name = "Status snapshot",
                    Description = "Show current AI status.",
                    Confidence = 0.70,
                    Methods =
                    [
                        new WorkflowMethodReference { Service = "OriginAIClient", Method = "GetStatusSnapshotAsync" }
                    ]
                }
            ]
        };

        var content = _generator.GenerateMarkdown(model, workflowSuggestions: suggestions);

        Assert.True(content.IndexOf("### Status snapshot", StringComparison.Ordinal) <
            content.IndexOf("### Password reset", StringComparison.Ordinal));
        Assert.Contains("- Risk: safe read-only", content);
        Assert.Contains("- Risk: high-risk/admin", content);
    }

    [Fact]
    public void GenerateMarkdown_DoesNotShowStaticCandidate_WhenEquivalentConfirmedActionExists()
    {
        var model = CreateModel() with
        {
            Actions =
            [
                new ActionModel
                {
                    Name = "Show Open Tickets",
                    SourceService = "SupportCapabilities",
                    MethodName = "ShowOpenTicketsAsync",
                    FilePath = "Workflows/SupportCapabilities.cs",
                    Classification = ActionClassification.Query,
                    Score = 1,
                    ExposureMode = ActionExposureMode.Confirmed
                },
                new ActionModel
                {
                    Name = "Show Open Tickets",
                    SourceService = "SupportTicketService",
                    MethodName = "ShowOpenTicketsAsync",
                    FilePath = "Services/SupportTicketService.cs",
                    Classification = ActionClassification.Query,
                    Score = 0.9,
                    ExposureMode = ActionExposureMode.Suggested
                },
                new ActionModel
                {
                    Name = "Prepare Restock Plan",
                    SourceService = "InventoryService",
                    MethodName = "PrepareRestockPlanAsync",
                    FilePath = "Services/InventoryService.cs",
                    Classification = ActionClassification.Workflow,
                    Score = 0.9,
                    ExposureMode = ActionExposureMode.Suggested
                }
            ]
        };

        var content = _generator.GenerateMarkdown(model);

        Assert.DoesNotContain("Existing method: `SupportTicketService.ShowOpenTicketsAsync`", content);
        Assert.Contains("Existing method: `InventoryService.PrepareRestockPlanAsync`", content);
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
