using Microsoft.Extensions.AI;

namespace AgentBlazor.Runtime;

public static class ComponentActionApprovalPolicy
{
    public static bool IsApprovalGranted(string componentId, string actionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        return IsApprovalGranted(componentId, actionId, ResolveExecutionContext());
    }

    public static bool IsApprovalGranted(
        string componentId,
        string actionId,
        IReadOnlyDictionary<string, string>? context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        if (context is null)
        {
            return false;
        }

        var scopedKey = $"agentblazor.approval.{componentId}.{actionId}";
        if (context.TryGetValue(scopedKey, out var scopedValue) && IsTruthy(scopedValue))
        {
            return true;
        }

        if (!context.TryGetValue("agentblazor.approvals", out var approvals) &&
            !context.TryGetValue("approvals", out approvals) &&
            !context.TryGetValue("approval_mode", out approvals))
        {
            return false;
        }

        if (IsTruthy(approvals))
        {
            return true;
        }

        var exactMatch = $"{componentId}.{actionId}";
        var entries = approvals.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return entries.Any(entry => entry.Equals(exactMatch, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyDictionary<string, string>? ResolveExecutionContext()
    {
        var properties = FunctionInvokingChatClient.CurrentContext?.Options?.AdditionalProperties;
        if (properties is null)
        {
            return null;
        }

        if (properties.TryGetValue("agentblazor_context", out var runtimeContextObj) &&
            runtimeContextObj is IDictionary<string, string> runtimeContext)
        {
            return runtimeContext.Count == 0
                ? null
                : new Dictionary<string, string>(runtimeContext, StringComparer.OrdinalIgnoreCase);
        }

        if (properties.TryGetValue("ag_ui_context", out var agUiContextObj) &&
            agUiContextObj is IEnumerable<KeyValuePair<string, string>> agUiContext)
        {
            var mapped = agUiContext
                .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(static pair => pair.Key, static pair => pair.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            return mapped.Count == 0 ? null : mapped;
        }

        return null;
    }

    private static bool IsTruthy(string value) =>
        value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("allow", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("approved", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("all", StringComparison.OrdinalIgnoreCase);
}
