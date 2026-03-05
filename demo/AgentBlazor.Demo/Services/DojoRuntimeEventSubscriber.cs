using AgentBlazor.Core.Runtime.Interfaces;

namespace AgentBlazor.Demo.Services;

internal sealed class DojoRuntimeEventSubscriber(DojoWorkspaceService workspaceService) : IAgentRuntimeEventSubscriber
{
    private const string DojoAgentName = "Dojo Workspace Agent";

    public async ValueTask OnToolExecutionFinishedAsync(
        AgentRuntimeToolExecutionFinishedEvent runtimeEvent,
        CancellationToken cancellationToken = default)
    {
        if (!IsDojoComponent(runtimeEvent.Result.ComponentId))
        {
            return;
        }

        var message = BuildToolExecutionMessage(runtimeEvent);
        await workspaceService.AppendRunNoteAsync(
            runtimeEvent.SessionId,
            message,
            runtimeEvent.OccurredAt.UtcDateTime,
            cancellationToken);
    }

    public async ValueTask OnErrorAsync(
        AgentRuntimeErrorEvent runtimeEvent,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(runtimeEvent.AgentName, DojoAgentName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await workspaceService.AppendRunNoteAsync(
            runtimeEvent.SessionId,
            $"Runtime error: {SanitizeDetail(runtimeEvent.ErrorMessage)}",
            runtimeEvent.OccurredAt.UtcDateTime,
            cancellationToken);
    }

    private static bool IsDojoComponent(string componentId)
        => componentId.StartsWith("dojo-", StringComparison.OrdinalIgnoreCase);

    private static string BuildToolExecutionMessage(AgentRuntimeToolExecutionFinishedEvent runtimeEvent)
    {
        var outcome = runtimeEvent.Result.Succeeded ? "Applied" : "Failed";
        var action = $"{runtimeEvent.Result.ComponentId}.{runtimeEvent.Result.ActionId}";
        var step = runtimeEvent.StepIndex + 1;
        var detail = SanitizeDetail(runtimeEvent.Result.Message);
        return $"{outcome}: {action} (step {step}). {detail}";
    }

    private static string SanitizeDetail(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "No additional details.";
        }

        var normalized = string.Join(" ", raw
            .Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (normalized.Length <= 220)
        {
            return normalized;
        }

        return normalized[..217] + "...";
    }
}
