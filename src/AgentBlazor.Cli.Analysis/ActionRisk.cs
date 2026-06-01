using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

public enum ActionRiskBand
{
    SafeReadOnly = 0,
    ReviewOutput = 1,
    ApprovalRequired = 2,
    HighRisk = 3
}

public static class ActionRisk
{
    private static readonly string[] HighRiskServiceTerms =
    [
        "Authentication",
        "Auth",
        "Email",
        "Message",
        "Mongo",
        "Password",
        "Permission",
        "TenantStore",
        "Transaction",
        "Workflow"
    ];

    private static readonly string[] HighRiskMethodTerms =
    [
        "Add",
        "Authenticate",
        "Create",
        "Delete",
        "Drop",
        "Execute",
        "Login",
        "Logout",
        "Remove",
        "Reset",
        "Run",
        "Send",
        "Submit",
        "Update",
        "Upsert"
    ];

    public static ActionRiskBand GetRiskBand(ActionModel action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsHighRisk(action))
        {
            return ActionRiskBand.HighRisk;
        }

        if (action.IsMutationLikely ||
            action.RequiresApproval ||
            action.Classification is ActionClassification.Command or ActionClassification.Workflow)
        {
            return ActionRiskBand.ApprovalRequired;
        }

        if (action.Classification == ActionClassification.Export)
        {
            return ActionRiskBand.ReviewOutput;
        }

        return ActionRiskBand.SafeReadOnly;
    }

    public static bool IsSafeReadOnly(ActionModel action)
        => GetRiskBand(action) == ActionRiskBand.SafeReadOnly;

    public static string Describe(ActionRiskBand band)
        => band switch
        {
            ActionRiskBand.SafeReadOnly => "safe read-only",
            ActionRiskBand.ReviewOutput => "review generated output",
            ActionRiskBand.ApprovalRequired => "approval required",
            ActionRiskBand.HighRisk => "high-risk/admin",
            _ => "unknown"
        };

    private static bool IsHighRisk(ActionModel action)
    {
        var serviceName = action.SourceService ?? string.Empty;
        var methodName = action.MethodName ?? string.Empty;
        var displayName = action.Name ?? string.Empty;

        if (ContainsAny(serviceName, HighRiskServiceTerms) &&
            ContainsAny(methodName, HighRiskMethodTerms))
        {
            return true;
        }

        if (ContainsAny(displayName, ["Password", "Tenant", "Permission", "Authenticate"]))
        {
            return true;
        }

        if (methodName.Contains("DropDatabase", StringComparison.OrdinalIgnoreCase) ||
            methodName.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
            methodName.Contains("Remove", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsAny(string value, IReadOnlyList<string> terms)
        => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
