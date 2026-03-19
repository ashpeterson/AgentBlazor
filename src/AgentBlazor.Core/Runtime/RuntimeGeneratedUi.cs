using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Core.Runtime;

internal static class RuntimeGeneratedUi
{
    public static AgentUiDocument? BuildDocument(
        IAgentUiToolCatalog uiToolCatalog,
        IReadOnlyList<AgentUiToolCall> toolCalls,
        Action<string>? logWarning = null)
    {
        if (toolCalls.Count == 0)
        {
            return null;
        }

        var generatedUi = uiToolCatalog.BuildDocument(toolCalls, out var renderErrors);
        if (generatedUi is null && renderErrors.Count > 0)
        {
            logWarning?.Invoke(string.Join("; ", renderErrors));
        }

        return generatedUi;
    }

    public static AgentTurnResponse Attach(
        AgentTurnResponse response,
        AgentUiDocument? generatedUi)
        => generatedUi is null
            ? response
            : response with { GeneratedUi = generatedUi };
}
