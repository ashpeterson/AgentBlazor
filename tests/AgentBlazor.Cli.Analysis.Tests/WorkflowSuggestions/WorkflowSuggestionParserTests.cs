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
        ]
    };
}
