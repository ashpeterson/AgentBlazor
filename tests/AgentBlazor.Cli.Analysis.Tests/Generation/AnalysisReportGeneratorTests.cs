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
    public void GenerateMarkdown_PutsPrioritizedGuidance_BeforeDetailedInventory()
    {
        var model = CreateModel();
        var suggestions = new WorkflowSuggestionSet
        {
            Model = "test-model",
            Suggestions =
            [
                new WorkflowSuggestion
                {
                    Name = "Find orders",
                    Description = "Find open orders.",
                    Methods =
                    [
                        new WorkflowMethodReference { Service = "OrderService", Method = "FindOrdersAsync" }
                    ],
                    Reasoning = "The orders page has a safe lookup method.",
                    Confidence = 0.9
                }
            ]
        };

        var content = _generator.GenerateMarkdown(model, workflowSuggestions: suggestions);

        Assert.True(content.IndexOf("## Top Recommendations", StringComparison.Ordinal) <
            content.IndexOf("## Routes And Pages", StringComparison.Ordinal));
        Assert.True(content.IndexOf("## Workflow Suggestions", StringComparison.Ordinal) <
            content.IndexOf("## Service Inventory", StringComparison.Ordinal));
        Assert.Contains("| Find orders | safe read-only | 0.90 | `OrderService.FindOrdersAsync` |", content);
    }

    [Fact]
    public void GenerateMarkdown_DemotesReadOnlyDataSuggestions_FromTopRecommendations_WhenProcessWorkflowExists()
    {
        var model = CreateModel() with
        {
            Actions =
            [
                .. CreateModel().Actions,
                new ActionModel
                {
                    Name = "Submit Asset",
                    SourceService = "RevisionSubmissionService",
                    MethodName = "SubmitAsync",
                    FilePath = "Services/RevisionSubmissionService.cs",
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Workflow,
                    IsMutationLikely = true,
                    RequiresApproval = true,
                    Score = 0.9
                },
                new ActionModel
                {
                    Name = "Get Asset Markers",
                    SourceService = "AssetMarkersService",
                    MethodName = "GetAssetMarkersAsync",
                    FilePath = "Services/AssetMarkersService.cs",
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Query,
                    Score = 0.95
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
                    Name = "View Asset Markers",
                    Description = "View asset markers.",
                    Methods =
                    [
                        new WorkflowMethodReference { Service = "AssetMarkersService", Method = "GetAssetMarkersAsync" }
                    ],
                    Confidence = 0.95
                },
                new WorkflowSuggestion
                {
                    Name = "Submit Asset for Review",
                    Description = "Submit an asset for review.",
                    Methods =
                    [
                        new WorkflowMethodReference { Service = "RevisionSubmissionService", Method = "SubmitAsync" }
                    ],
                    Confidence = 0.75
                }
            ]
        };

        var content = _generator.GenerateMarkdown(model, workflowSuggestions: suggestions);
        var topRecommendations = ExtractSection(content, "## Top Recommendations", "## Install Blockers");

        Assert.Contains("Submit Asset for Review", topRecommendations);
        Assert.DoesNotContain("View Asset Markers", topRecommendations);
        Assert.Contains("1 read-only data/view suggestion(s) were not promoted", topRecommendations);
        Assert.Contains("### View Asset Markers", content);
    }

    [Fact]
    public void GenerateMarkdown_DoesNotPromotePureReadOnlySuggestions_ToTopRecommendations()
    {
        var suggestions = new WorkflowSuggestionSet
        {
            Model = "test-model",
            Suggestions =
            [
                new WorkflowSuggestion
                {
                    Name = "View Asset Markers",
                    Description = "View asset markers.",
                    Methods =
                    [
                        new WorkflowMethodReference { Service = "OrderService", Method = "FindOrdersAsync" }
                    ],
                    Confidence = 0.95
                }
            ]
        };

        var content = _generator.GenerateMarkdown(CreateModel(), workflowSuggestions: suggestions);
        var topRecommendations = ExtractSection(content, "## Top Recommendations", "## Install Blockers");

        Assert.Contains("No process-oriented workflow candidates were found", topRecommendations);
        Assert.DoesNotContain("| View Asset Markers |", topRecommendations);
        Assert.Contains("### View Asset Markers", content);
    }

    [Fact]
    public void GenerateMarkdown_IncludesCompactInstallBlockers_BeforeDetailedReadiness()
    {
        var readiness = new InstallReadinessReport
        {
            HostProjectName = "TestApp",
            HostProjectPath = "/tmp/TestApp/TestApp.csproj",
            HostShape = new HostShapeAssessment { Title = "Standard Blazor Web App" },
            Checks =
            [
                new InstallReadinessCheck
                {
                    Id = "package-references",
                    Title = "Package references",
                    Status = InstallReadinessStatus.Missing,
                    Message = "The host project does not reference AgentBlazor yet.",
                    SuggestedFix = "Add the AgentBlazor package."
                },
                new InstallReadinessCheck
                {
                    Id = "mud-services",
                    Title = "MudBlazor service registration",
                    Status = InstallReadinessStatus.Pass,
                    Message = "Found AddMudServices()."
                }
            ]
        };

        var content = _generator.GenerateMarkdown(CreateModel(), readiness);

        Assert.True(content.IndexOf("## Install Blockers", StringComparison.Ordinal) <
            content.IndexOf("## Routes And Pages", StringComparison.Ordinal));
        Assert.True(content.IndexOf("## Install Blockers", StringComparison.Ordinal) <
            content.IndexOf("## Install Readiness", StringComparison.Ordinal));
        Assert.Contains("| Missing | Package references | Add the AgentBlazor package. |", content);
        Assert.DoesNotContain("| Pass | MudBlazor service registration | Found AddMudServices(). |", content[..content.IndexOf("## Routes And Pages", StringComparison.Ordinal)]);
    }

    [Fact]
    public void GenerateMarkdown_WarnsWhenMostRoutesDoNotMapToActions()
    {
        var model = CreateModel() with
        {
            Routes =
            [
                .. Enumerable.Range(1, 8).Select(index => new RouteModel
                {
                    Template = $"/page-{index}",
                    ComponentName = $"Page{index}",
                    ComponentFile = $"Pages/Page{index}.razor"
                })
            ],
            Pages =
            [
                new PageModel
                {
                    Route = "/page-1",
                    ComponentName = "Page1",
                    SuggestedActions = ["order_service.find_orders"]
                }
            ],
            Actions =
            [
                new ActionModel
                {
                    Id = "order_service.find_orders",
                    Name = "Find Orders",
                    SourceService = "OrderService",
                    MethodName = "FindOrdersAsync",
                    FilePath = "Services/OrderService.cs",
                    Classification = ActionClassification.Query,
                    Score = 0.92,
                    ExposureMode = ActionExposureMode.Suggested
                }
            ]
        };

        var content = _generator.GenerateMarkdown(model);

        Assert.Contains("Route quality note: only 1 of 8 route(s) mapped to candidate actions", content);
        Assert.Contains("dynamic, multi-tenant, or component-composed apps", content);
    }

    [Fact]
    public void GenerateMarkdown_ClassifiesServiceInventory_ByAgentFit()
    {
        var model = CreateModel() with
        {
            Services =
            [
                .. CreateModel().Services,
                new ServiceModel
                {
                    TypeName = "MongoService",
                    FilePath = "Services/MongoDb/MongoService.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "AggregateAsync",
                            ReturnType = "Task<List<T>>",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "PermissionsService",
                    FilePath = "Services/Permissions/PermissionsService.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "AddPermission",
                            ReturnType = "Task",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "EmailService",
                    FilePath = "Services/Email/EmailService.cs",
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
                .. CreateModel().Actions,
                new ActionModel
                {
                    Name = "Aggregate",
                    SourceService = "MongoService",
                    MethodName = "AggregateAsync",
                    FilePath = "Services/MongoDb/MongoService.cs",
                    Classification = ActionClassification.Query,
                    Score = 0.8,
                    ExposureMode = ActionExposureMode.Suggested
                },
                new ActionModel
                {
                    Name = "Add Permission",
                    SourceService = "PermissionsService",
                    MethodName = "AddPermission",
                    FilePath = "Services/Permissions/PermissionsService.cs",
                    Classification = ActionClassification.Command,
                    IsMutationLikely = true,
                    Score = 0.8,
                    ExposureMode = ActionExposureMode.Suggested
                },
                new ActionModel
                {
                    Name = "Send Email",
                    SourceService = "EmailService",
                    MethodName = "SendEmailAsync",
                    FilePath = "Services/Email/EmailService.cs",
                    Classification = ActionClassification.Command,
                    IsMutationLikely = true,
                    Score = 0.8,
                    ExposureMode = ActionExposureMode.Suggested
                }
            ]
        };

        var content = _generator.GenerateMarkdown(model);

        Assert.Contains("| Business/read-only | 1 | Best starting point for first agent actions. |", content);
        Assert.Contains("| Integration/messaging | 1 | Useful, but external effects or delivery need policy review. |", content);
        Assert.Contains("| Data access | 1 | Prefer wrapping in business capabilities rather than exposing directly. |", content);
        Assert.Contains("| Admin/sensitive | 1 | Do not expose directly without explicit approval, auth, and tenant policy. |", content);
        Assert.Contains("### `MongoService`", content);
        Assert.Contains("- Classification: Data access", content);
        Assert.Contains("### `PermissionsService`", content);
        Assert.Contains("- Classification: Admin/sensitive", content);
        Assert.Contains("### `EmailService`", content);
        Assert.Contains("- Classification: Integration/messaging", content);
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
                },
                new ServiceModel
                {
                    TypeName = "CouponRepository",
                    FilePath = "Modules/Pricing/Infrastructure/EFCore/Repositories/CouponRepository.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "GetAllAsync",
                            ReturnType = "Task<IEnumerable<Coupon>>",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "InventoryMovementCreatedEventHandler",
                    FilePath = "Modules/Inventory/Application/EventHandlers/InventoryMovementCreatedEventHandler.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "Handle",
                            ReturnType = "Task",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "JwtService",
                    FilePath = "Auth/Services/JwtService.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "GenerateTokensAsync",
                            ReturnType = "Task<TokensResult>",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "AmazonS3StorageManager",
                    FilePath = "ClassifiedAds.Infrastructure/Storages/Amazon/AmazonS3StorageManager.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "ArchiveAsync",
                            ReturnType = "Task",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "ExportProductsToPdfHandler",
                    FilePath = "ClassifiedAds.Infrastructure/Pdf/DinkToPdf/ExportProductsToPdfHandler.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "WriteAsync",
                            ReturnType = "Task",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "TokenManager",
                    FilePath = "ClassifiedAds.Infrastructure/Web/Authentication/TokenManager.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "RefreshToken",
                            ReturnType = "Task<TokenModel>",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "UserStore",
                    FilePath = "ClassifiedAds.Infrastructure/Identity/UserStore.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "FindByEmailAsync",
                            ReturnType = "Task<User>",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "AuthorizedHttpClientService",
                    FilePath = "Shared/Client/BuildingBlocks/Http/AuthorizedHttpClientService.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "GetFromAPIAsync",
                            ReturnType = "Task<T>",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "HttpService",
                    FilePath = "Blazor.Modules/Core/Services/HttpService.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "GetAccessToken",
                            ReturnType = "Task<string>",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "EFCoreConfigurationValidator",
                    FilePath = "Shared/Features/EFCore/Configuration/EFCoreConfigurationValidator.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "Validate",
                            ReturnType = "ValidateOptionsResult",
                            IsPublic = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "NotificationHubService",
                    FilePath = "Shared/Features/SignalR/NotificationHubService.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "SendNotificationAsync",
                            ReturnType = "Task",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "ServerExecutionContext",
                    FilePath = "Shared/Features/Misc/ExecutionContext/ServerExecutionContext.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "CreateInstance",
                            ReturnType = "ServerExecutionContext",
                            IsPublic = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "UserManager",
                    FilePath = "Oqtane.Server/Managers/UserManager.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "ValidateUser",
                            ReturnType = "Task<UserValidateResult>",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "JwtManager",
                    FilePath = "Oqtane.Server/Security/JwtManager.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "GenerateToken",
                            ReturnType = "string",
                            IsPublic = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "PageState",
                    FilePath = "Oqtane.Client/UI/PageState.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "Clone",
                            ReturnType = "PageState",
                            IsPublic = true
                        }
                    ]
                },
                new ServiceModel
                {
                    TypeName = "UserService",
                    FilePath = "Services/UserService.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "ValidateUserAsync",
                            ReturnType = "Task<UserValidateResult>",
                            IsPublic = true,
                            IsAsync = true
                        },
                        new ServiceMethodModel
                        {
                            Name = "GetTokenAsync",
                            ReturnType = "Task<string>",
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
                },
                new ActionModel
                {
                    Name = "Get All Coupons",
                    SourceService = "CouponRepository",
                    MethodName = "GetAllAsync",
                    FilePath = "Modules/Pricing/Infrastructure/EFCore/Repositories/CouponRepository.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Query
                },
                new ActionModel
                {
                    Name = "Handle",
                    SourceService = "InventoryMovementCreatedEventHandler",
                    MethodName = "Handle",
                    FilePath = "Modules/Inventory/Application/EventHandlers/InventoryMovementCreatedEventHandler.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Unknown
                },
                new ActionModel
                {
                    Name = "Generate Tokens",
                    SourceService = "JwtService",
                    MethodName = "GenerateTokensAsync",
                    FilePath = "Auth/Services/JwtService.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Export
                },
                new ActionModel
                {
                    Name = "Archive",
                    SourceService = "AmazonS3StorageManager",
                    MethodName = "ArchiveAsync",
                    FilePath = "ClassifiedAds.Infrastructure/Storages/Amazon/AmazonS3StorageManager.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Command,
                    IsMutationLikely = true
                },
                new ActionModel
                {
                    Name = "Write",
                    SourceService = "ExportProductsToPdfHandler",
                    MethodName = "WriteAsync",
                    FilePath = "ClassifiedAds.Infrastructure/Pdf/DinkToPdf/ExportProductsToPdfHandler.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Command
                },
                new ActionModel
                {
                    Name = "Refresh Token",
                    SourceService = "TokenManager",
                    MethodName = "RefreshToken",
                    FilePath = "ClassifiedAds.Infrastructure/Web/Authentication/TokenManager.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Workflow,
                    IsMutationLikely = true
                },
                new ActionModel
                {
                    Name = "Find By Email",
                    SourceService = "UserStore",
                    MethodName = "FindByEmailAsync",
                    FilePath = "ClassifiedAds.Infrastructure/Identity/UserStore.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Query
                },
                new ActionModel
                {
                    Name = "Get From A P I",
                    SourceService = "AuthorizedHttpClientService",
                    MethodName = "GetFromAPIAsync",
                    FilePath = "Shared/Client/BuildingBlocks/Http/AuthorizedHttpClientService.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Query
                },
                new ActionModel
                {
                    Name = "Get Access Token",
                    SourceService = "HttpService",
                    MethodName = "GetAccessToken",
                    FilePath = "Blazor.Modules/Core/Services/HttpService.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Query
                },
                new ActionModel
                {
                    Name = "Validate",
                    SourceService = "EFCoreConfigurationValidator",
                    MethodName = "Validate",
                    FilePath = "Shared/Features/EFCore/Configuration/EFCoreConfigurationValidator.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Validation
                },
                new ActionModel
                {
                    Name = "Send Notification",
                    SourceService = "NotificationHubService",
                    MethodName = "SendNotificationAsync",
                    FilePath = "Shared/Features/SignalR/NotificationHubService.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Command,
                    IsMutationLikely = true
                },
                new ActionModel
                {
                    Name = "Create Instance",
                    SourceService = "ServerExecutionContext",
                    MethodName = "CreateInstance",
                    FilePath = "Shared/Features/Misc/ExecutionContext/ServerExecutionContext.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Command
                },
                new ActionModel
                {
                    Name = "Validate User",
                    SourceService = "UserManager",
                    MethodName = "ValidateUser",
                    FilePath = "Oqtane.Server/Managers/UserManager.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Validation
                },
                new ActionModel
                {
                    Name = "Generate Token",
                    SourceService = "JwtManager",
                    MethodName = "GenerateToken",
                    FilePath = "Oqtane.Server/Security/JwtManager.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Export
                },
                new ActionModel
                {
                    Name = "Clone",
                    SourceService = "PageState",
                    MethodName = "Clone",
                    FilePath = "Oqtane.Client/UI/PageState.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Command
                },
                new ActionModel
                {
                    Name = "Validate User",
                    SourceService = "UserService",
                    MethodName = "ValidateUserAsync",
                    FilePath = "Services/UserService.cs",
                    Score = 0.99,
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Validation
                },
                new ActionModel
                {
                    Name = "Get Token",
                    SourceService = "UserService",
                    MethodName = "GetTokenAsync",
                    FilePath = "Services/UserService.cs",
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
        Assert.DoesNotContain("CouponRepository", content);
        Assert.DoesNotContain("Get All Coupons", content);
        Assert.DoesNotContain("InventoryMovementCreatedEventHandler", content);
        Assert.DoesNotContain("Generate Tokens", content);
        Assert.DoesNotContain("AmazonS3StorageManager", content);
        Assert.DoesNotContain("ArchiveAsync", content);
        Assert.DoesNotContain("ExportProductsToPdfHandler", content);
        Assert.DoesNotContain("WriteAsync", content);
        Assert.DoesNotContain("TokenManager", content);
        Assert.DoesNotContain("Refresh Token", content);
        Assert.DoesNotContain("UserStore", content);
        Assert.DoesNotContain("Find By Email", content);
        Assert.DoesNotContain("AuthorizedHttpClientService", content);
        Assert.DoesNotContain("Get From A P I", content);
        Assert.DoesNotContain("HttpService", content);
        Assert.DoesNotContain("Get Access Token", content);
        Assert.DoesNotContain("EFCoreConfigurationValidator", content);
        Assert.DoesNotContain("ValidateOptionsResult", content);
        Assert.DoesNotContain("NotificationHubService", content);
        Assert.DoesNotContain("Send Notification", content);
        Assert.DoesNotContain("ServerExecutionContext", content);
        Assert.DoesNotContain("Create Instance", content);
        Assert.DoesNotContain("UserManager", content);
        Assert.DoesNotContain("Validate User", content);
        Assert.DoesNotContain("JwtManager", content);
        Assert.DoesNotContain("Generate Token", content);
        Assert.DoesNotContain("PageState", content);
        Assert.DoesNotContain("ValidateUserAsync", content);
        Assert.DoesNotContain("Validate User", content);
        Assert.DoesNotContain("GetTokenAsync", content);
        Assert.DoesNotContain("Get Token", content);
        Assert.Contains("OrderService", content);
        Assert.Contains("FindOrdersAsync", content);
    }

    [Fact]
    public void GenerateMarkdown_ExplainsComponentDrivenRouting_WhenComponentsHaveNoPageRoutes()
    {
        var model = CreateModel() with
        {
            Routes = [],
            Pages = [],
            Components =
            [
                new ComponentModel
                {
                    Id = "admin_users_index",
                    Name = "Index",
                    FilePath = "Modules/Admin/Users/Index.razor",
                    IsPage = false
                }
            ]
        };

        var content = _generator.GenerateMarkdown(model);

        Assert.Contains("No Razor `@page` routes were discovered.", content);
        Assert.Contains("component-driven or framework-managed routing", content);
        Assert.Contains("route-to-action linking is unavailable", content);
    }

    [Fact]
    public void GenerateMarkdown_ReportsNoDeveloperFacingServices_WhenAllDiscoveredServicesAreFiltered()
    {
        var model = CreateModel() with
        {
            Services =
            [
                new ServiceModel
                {
                    TypeName = "AuthorizedHttpClientService",
                    FilePath = "Shared/Client/BuildingBlocks/Http/AuthorizedHttpClientService.cs",
                    Methods =
                    [
                        new ServiceMethodModel
                        {
                            Name = "GetFromAPIAsync",
                            ReturnType = "Task<T>",
                            IsPublic = true,
                            IsAsync = true
                        }
                    ]
                }
            ],
            Actions = []
        };

        var content = _generator.GenerateMarkdown(model);

        Assert.Contains("No developer-facing service-like classes were discovered.", content);
        Assert.DoesNotContain("### `AuthorizedHttpClientService`", content);
    }

    [Fact]
    public void GenerateMarkdown_UsesValidatedWorkflowSuggestions_InsteadOfStaticActionRecommendations()
    {
        var model = CreateModel() with
        {
            Recommendations =
            [
                new RecommendationModel
                {
                    Type = RecommendationType.AddAgentAction,
                    TargetName = "OrderService.FindOrdersAsync",
                    Suggestion = """Add `[AgentAction("Find Orders")]`""",
                    Priority = 0.9
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
                    Name = "Find orders",
                    Description = "Find open orders.",
                    Methods =
                    [
                        new WorkflowMethodReference { Service = "OrderService", Method = "FindOrdersAsync" }
                    ],
                    Confidence = 0.9
                }
            ]
        };

        var content = _generator.GenerateMarkdown(model, workflowSuggestions: suggestions);

        Assert.Contains("Review the validated workflow suggestions above", content);
        Assert.DoesNotContain("""Add `[AgentAction("Find Orders")]`""", content);
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

        Assert.True(content.IndexOf("### Password reset", StringComparison.Ordinal) <
            content.IndexOf("### Status snapshot", StringComparison.Ordinal));
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

    private static string ExtractSection(string content, string startHeading, string endHeading)
    {
        var start = content.IndexOf(startHeading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find section start `{startHeading}`.");
        var end = content.IndexOf(endHeading, start + startHeading.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not find section end `{endHeading}`.");
        return content[start..end];
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
