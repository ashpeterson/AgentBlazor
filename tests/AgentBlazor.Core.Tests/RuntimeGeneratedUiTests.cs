using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;

namespace AgentBlazor.Core.Tests;

public sealed class RuntimeGeneratedUiTests
{
    [Fact]
    public void BuildDocument_LogsWarningsWhenRenderingFails()
    {
        var catalog = new StubAgentUiToolCatalog(
            document: null,
            errors: ["first error", "second error"]);
        var warnings = new List<string>();

        var document = RuntimeGeneratedUi.BuildDocument(
            catalog,
            [new AgentUiToolCall { ToolId = "summary.card" }],
            warnings.Add);

        Assert.Null(document);
        Assert.Single(warnings);
        Assert.Equal("first error; second error", warnings[0]);
    }

    [Fact]
    public void Attach_AddsGeneratedUiToResponse()
    {
        var response = new AgentTurnResponse(
            "agent",
            "done",
            [],
            Array.Empty<ComponentActionExecutionResult>());
        var document = new AgentUiDocument
        {
            Blocks =
            [
                new AgentUiBlock
                {
                    Id = "summary",
                    Kind = AgentUiBlockKind.Card,
                    Title = "Summary"
                }
            ]
        };

        var attached = RuntimeGeneratedUi.Attach(response, document);

        Assert.Same(document, attached.GeneratedUi);
        Assert.Equal(response.ResponseText, attached.ResponseText);
    }

    private sealed class StubAgentUiToolCatalog(
        AgentUiDocument? document,
        IReadOnlyList<string> errors) : IAgentUiToolCatalog
    {
        private readonly AgentUiDocument? _document = document;
        private readonly IReadOnlyList<string> _errors = errors;

        public IReadOnlyList<AgentUiToolDescriptor> GetTools() => [];

        public AgentUiDocument? BuildDocument(
            IReadOnlyList<AgentUiToolCall> toolCalls,
            out IReadOnlyList<string> errors)
        {
            errors = _errors;
            return _document;
        }
    }
}
