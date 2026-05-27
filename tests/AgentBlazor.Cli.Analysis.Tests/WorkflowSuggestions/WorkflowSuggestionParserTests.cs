using AgentBlazor.Cli.Analysis.Models;
using AgentBlazor.Cli.Analysis.WorkflowSuggestions;

namespace AgentBlazor.Cli.Analysis.Tests.WorkflowSuggestions;

public sealed class WorkflowSuggestionParserTests
{
    [Fact]
    public void ParseAndValidate_AcceptsSuggestionsReferencingKnownMethods()
    {
        var parser = new WorkflowSuggestionParser();
        var model = CreateModel();
        var json = """
        {
          "workflows": [
            {
              "name": "Order follow-up",
              "description": "Find open orders and draft a follow-up.",
              "methods": [
                { "service": "OrderService", "method": "FindOrdersAsync" },
                { "service": "OrderService", "method": "DraftFollowUpAsync" }
              ],
              "capabilityClass": "OrderFollowUpCapabilities",
              "code": "public sealed class OrderFollowUpCapabilities {}",
              "reasoning": "The route and service methods align.",
              "confidence": 0.86
            }
          ]
        }
        """;

        var result = parser.ParseAndValidate(json, model, "test-model");

        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal("Order follow-up", suggestion.Name);
        Assert.Equal(2, suggestion.Methods.Count);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void ParseAndValidate_RemovesIllustrativeCode_WhenItDoesNotUseCapabilityResult()
    {
        var parser = new WorkflowSuggestionParser();
        var model = CreateModel();
        var json = """
        {
          "workflows": [
            {
              "name": "Order lookup",
              "description": "Find open orders.",
              "methods": [
                { "service": "OrderService", "method": "FindOrdersAsync" }
              ],
              "capabilityClass": "OrderLookupCapabilities",
              "code": "public sealed class OrderLookupCapabilities { public async Task<List<Order>> FindOrders() { ... } }",
              "reasoning": "The route and service method align.",
              "confidence": 0.82
            }
          ]
        }
        """;

        var result = parser.ParseAndValidate(json, model, "test-model");

        var suggestion = Assert.Single(result.Suggestions);
        Assert.Empty(suggestion.Code);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void ParseAndValidate_RejectsSuggestions_WhenAllReferencedMethodsAlreadyHaveConfirmedActions()
    {
        var parser = new WorkflowSuggestionParser();
        var model = CreateModel() with
        {
            Actions =
            [
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
                    Name = "Find Orders",
                    SourceService = "OrderCapabilities",
                    MethodName = "FindOrdersAsync",
                    FilePath = "Workflows/OrderCapabilities.cs",
                    ExposureMode = ActionExposureMode.Confirmed,
                    Classification = ActionClassification.Query
                }
            ]
        };
        var json = """
        {
          "workflows": [
            {
              "name": "Order lookup",
              "description": "Find open orders.",
              "methods": [
                { "service": "OrderService", "method": "FindOrdersAsync" }
              ],
              "confidence": 0.82
            }
          ]
        }
        """;

        var result = parser.ParseAndValidate(json, model, "test-model");

        Assert.Empty(result.Suggestions);
        var rejected = Assert.Single(result.Rejected);
        Assert.Contains("already have confirmed AgentBlazor actions", rejected.Reason);
    }

    [Fact]
    public void ParseAndValidate_RejectsSuggestionsReferencingUnknownMethods()
    {
        var parser = new WorkflowSuggestionParser();
        var model = CreateModel();
        var json = """
        {
          "workflows": [
            {
              "name": "Invented workflow",
              "description": "Uses a method that does not exist.",
              "methods": [
                { "service": "OrderService", "method": "DeleteEverythingAsync" }
              ],
              "confidence": 0.91
            }
          ]
        }
        """;

        var result = parser.ParseAndValidate(json, model, "test-model");

        Assert.Empty(result.Suggestions);
        var rejected = Assert.Single(result.Rejected);
        Assert.Contains("OrderService.DeleteEverythingAsync", rejected.Reason);
    }

    [Fact]
    public void ParseAndValidate_RejectsSuggestions_WhenMethodTermsDoNotMatchWorkflow()
    {
        var parser = new WorkflowSuggestionParser();
        var model = CreateModel() with
        {
            Actions =
            [
                new ActionModel
                {
                    Name = "Get Access Token",
                    SourceService = "AccessTokenService",
                    MethodName = "GetAccessTokenAsync",
                    FilePath = "Services/AccessTokenService.cs",
                    ExposureMode = ActionExposureMode.Suggested,
                    Classification = ActionClassification.Query,
                    Score = 0.8
                }
            ]
        };
        var json = """
        {
          "workflows": [
            {
              "name": "Role assignment",
              "description": "Assign roles to users.",
              "methods": [
                { "service": "AccessTokenService", "method": "GetAccessTokenAsync" }
              ],
              "confidence": 0.82
            }
          ]
        }
        """;

        var result = parser.ParseAndValidate(json, model, "test-model");

        Assert.Empty(result.Suggestions);
        var rejected = Assert.Single(result.Rejected);
        Assert.Contains("do not align", rejected.Reason);
        Assert.Contains("AccessTokenService.GetAccessTokenAsync", rejected.Reason);
    }

    [Fact]
    public void ParseAndValidate_AcceptsMethodReferencesThatIncludeParameterLists()
    {
        var parser = new WorkflowSuggestionParser();
        var model = CreateModel();
        var json = """
        {
          "workflows": [
            {
              "name": "Order follow-up",
              "description": "Find open orders and draft follow-up messages.",
              "methods": [
                { "service": "OrderService", "method": "FindOrdersAsync(DateOnly since)" },
                "OrderService.DraftFollowUpAsync(string orderId)"
              ],
              "confidence": 0.80
            }
          ]
        }
        """;

        var result = parser.ParseAndValidate(json, model, "test-model");

        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal("FindOrdersAsync", suggestion.Methods[0].Method);
        Assert.Equal("DraftFollowUpAsync", suggestion.Methods[1].Method);
        Assert.Empty(result.Rejected);
    }

    private static ProjectModel CreateModel() => new()
    {
        AppName = "OrderApp",
        BlazorHostProject = "OrderApp",
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
                        Name = "DraftFollowUpAsync",
                        ReturnType = "Task<string>",
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
                Name = "Draft Follow Up",
                SourceService = "OrderService",
                MethodName = "DraftFollowUpAsync",
                FilePath = "Services/OrderService.cs",
                ExposureMode = ActionExposureMode.Suggested,
                Classification = ActionClassification.Workflow,
                Score = 0.8
            }
        ]
    };
}
