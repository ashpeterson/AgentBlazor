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
                }
            ]
        };

        var prompt = builder.Build(model);

        Assert.Contains("OrderService", prompt);
        Assert.Contains("FindOrdersAsync", prompt);
        Assert.DoesNotContain("AgentBlazorBuilder", prompt);
        Assert.DoesNotContain("SetSelectedNodeAsync", prompt);
    }
}
