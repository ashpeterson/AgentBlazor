using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentBlazor.Core.Runtime.Agents;

public static class PendingApprovalIds
{
    public static string Resolve(PendingApproval approval)
        => !string.IsNullOrWhiteSpace(approval.ApprovalId)
            ? approval.ApprovalId
            : Create(approval.ComponentId, approval.ActionId, approval.Parameters);

    public static string Create(
        string componentId,
        string actionId,
        IReadOnlyDictionary<string, object?> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        var canonicalParameters = parameters
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
        var serialized = JsonSerializer.Serialize(canonicalParameters);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)))[..16].ToLowerInvariant();
        return $"{componentId}.{actionId}#{hash}";
    }

    public static string BuildTargetKey(string componentId, string actionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        return $"{componentId}.{actionId}";
    }
}
