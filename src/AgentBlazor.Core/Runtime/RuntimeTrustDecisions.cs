using AgentBlazor.Execution;

namespace AgentBlazor.Core.Runtime;

internal static class RuntimeTrustDecisions
{
    public static AgentPolicyDecision BuildPolicyDecision(
        string targetId,
        string actionId,
        bool requiresApproval)
    {
        if (requiresApproval)
        {
            return new AgentPolicyDecision(
                Allowed: true,
                RiskClass: ResolveRiskClass(targetId, actionId, requiresApproval: true),
                ApprovalMode: AgentApprovalMode.ExplicitPlanApproval,
                Reason: $"Approval is required for {targetId}.{actionId}.");
        }

        return new AgentPolicyDecision(
            Allowed: true,
            RiskClass: ResolveRiskClass(targetId, actionId, requiresApproval: false),
            ApprovalMode: AgentApprovalMode.None);
    }

    public static AgentContextFreshness ResolveFreshness(string? contextVersion)
        => string.IsNullOrWhiteSpace(contextVersion)
            ? AgentContextFreshness.Unknown
            : AgentContextFreshness.Current;

    private static AgentRiskClass ResolveRiskClass(
        string targetId,
        string actionId,
        bool requiresApproval)
    {
        if (requiresApproval)
        {
            return AgentRiskClass.SignificantMutation;
        }

        if (targetId.StartsWith("Agent", StringComparison.OrdinalIgnoreCase) &&
            (actionId.Contains("submit", StringComparison.OrdinalIgnoreCase) ||
             actionId.Contains("confirm", StringComparison.OrdinalIgnoreCase) ||
             actionId.Contains("delete", StringComparison.OrdinalIgnoreCase)))
        {
            return AgentRiskClass.LowRiskMutation;
        }

        return AgentRiskClass.ReadOnly;
    }
}
