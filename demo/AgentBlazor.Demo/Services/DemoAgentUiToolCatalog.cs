using System.Globalization;
using AgentBlazor.Core.Components;

namespace AgentBlazor.Demo.Services;

internal static class DemoAgentUiToolIds
{
    public const string SuppliersHighestRiskFocus = "suppliers.highest_risk_focus";
    public const string OnboardingDraft = "onboarding.draft";
    public const string OnboardingApplied = "onboarding.applied";
}

internal sealed class DemoAgentUiToolCatalog : IAgentUiToolCatalog
{
    private static readonly IReadOnlyList<AgentUiToolDescriptor> DemoTools =
    [
        new AgentUiToolDescriptor
        {
            ToolId = DemoAgentUiToolIds.SuppliersHighestRiskFocus,
            Description = "Render supplier high-risk focus card with deterministic follow-up controls.",
            InputSchema = """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "supplierName": { "type": "string" },
                    "riskScore": { "type": ["number", "integer"] }
                  }
                }
                """
        },
        new AgentUiToolDescriptor
        {
            ToolId = DemoAgentUiToolIds.OnboardingDraft,
            Description = "Render supplier onboarding draft form with payload-forwarding apply action.",
            InputSchema = """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "supplierName": { "type": "string" },
                    "riskTier": { "type": "string" }
                  },
                  "required": ["supplierName"]
                }
                """
        },
        new AgentUiToolDescriptor
        {
            ToolId = DemoAgentUiToolIds.OnboardingApplied,
            Description = "Render onboarding confirmation card after applying form values.",
            InputSchema = """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "supplierName": { "type": "string" }
                  }
                }
                """
        }
    ];

    private readonly IAgentUiToolCatalog _coreCatalog = new DefaultAgentUiToolCatalog();
    private readonly IReadOnlyList<AgentUiToolDescriptor> _allTools;
    private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> SupplierRiskRows =
    [
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SupplierName"] = "Farside Plastics",
            ["Region"] = "APAC",
            ["RiskScore"] = 91,
            ["Trend"] = "Rising"
        },
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SupplierName"] = "North Ridge Metals",
            ["Region"] = "EU",
            ["RiskScore"] = 84,
            ["Trend"] = "Rising"
        },
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SupplierName"] = "Summit Controls",
            ["Region"] = "US",
            ["RiskScore"] = 77,
            ["Trend"] = "Stable"
        },
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SupplierName"] = "Harbor Electric",
            ["Region"] = "US",
            ["RiskScore"] = 69,
            ["Trend"] = "Stable"
        },
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SupplierName"] = "Delta Industrial",
            ["Region"] = "LATAM",
            ["RiskScore"] = 58,
            ["Trend"] = "Improving"
        }
    ];

    public DemoAgentUiToolCatalog()
    {
        var combined = new List<AgentUiToolDescriptor>(_coreCatalog.GetTools().Count + DemoTools.Count);
        combined.AddRange(_coreCatalog.GetTools());
        combined.AddRange(DemoTools);
        _allTools = combined;
    }

    public IReadOnlyList<AgentUiToolDescriptor> GetTools() => _allTools;

    public AgentUiDocument? BuildDocument(
        IReadOnlyList<AgentUiToolCall> toolCalls,
        out IReadOnlyList<string> errors)
    {
        var translated = toolCalls
            .Select(TranslateToolCall)
            .ToArray();

        return _coreCatalog.BuildDocument(translated, out errors);
    }

    private static AgentUiToolCall TranslateToolCall(AgentUiToolCall toolCall)
    {
        if (string.IsNullOrWhiteSpace(toolCall.ToolId))
        {
            return toolCall;
        }

        var args = NormalizeArguments(toolCall.Arguments);

        return toolCall.ToolId.Trim().ToLowerInvariant() switch
        {
            DemoAgentUiToolIds.SuppliersHighestRiskFocus => BuildHighestRiskFocusCall(args),
            DemoAgentUiToolIds.OnboardingDraft => BuildOnboardingDraftCall(args),
            DemoAgentUiToolIds.OnboardingApplied => BuildOnboardingAppliedCall(args),
            _ => toolCall
        };
    }

    private static AgentUiToolCall BuildHighestRiskFocusCall(IReadOnlyDictionary<string, object?> args)
    {
        var mode = ReadString(args, "mode");
        var showOnlyHighRisk = string.Equals(mode, "highOnly", StringComparison.OrdinalIgnoreCase);
        var rows = SupplierRiskRows
            .Where(row => !showOnlyHighRisk || ReadRiskScore(row) >= 70)
            .OrderByDescending(ReadRiskScore)
            .ToList();
        var topSupplier = rows.FirstOrDefault();
        var topSupplierName = topSupplier is null ? null : ReadString(topSupplier, "SupplierName");
        var topRiskScore = topSupplier is null ? null : ReadDouble(topSupplier, "RiskScore");
        var description = showOnlyHighRisk
            ? "Showing suppliers with RiskScore >= 70."
            : !string.IsNullOrWhiteSpace(topSupplierName) && topRiskScore is not null
                ? $"Top supplier is '{topSupplierName}' (RiskScore {topRiskScore.Value.ToString("0.##", CultureInfo.InvariantCulture)})."
                : "Sorted suppliers by RiskScore descending.";

        return new AgentUiToolCall
        {
            ToolId = AgentUiToolIds.TableView,
            Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["blockId"] = showOnlyHighRisk ? "high-risk-suppliers-table" : "supplier-risk-table",
                ["title"] = showOnlyHighRisk ? "High Risk Suppliers" : "Supplier Risk Snapshot",
                ["description"] = description,
                ["columns"] = new object?[]
                {
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["key"] = "SupplierName",
                        ["header"] = "Supplier"
                    },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["key"] = "Region",
                        ["header"] = "Region"
                    },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["key"] = "RiskScore",
                        ["header"] = "Risk Score"
                    },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["key"] = "Trend",
                        ["header"] = "Trend"
                    }
                },
                ["rows"] = rows.Cast<object?>().ToArray(),
                ["actions"] = new object?[]
                {
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["id"] = "refreshHighestRisk",
                        ["label"] = "Run Again",
                        ["prompt"] = "Refresh the supplier risk snapshot."
                    },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["id"] = "showOnlyHighRisk",
                        ["label"] = "Filter High Risk",
                        ["prompt"] = "Show only suppliers with risk score 70 and above."
                    }
                }
            }
        };
    }

    private static AgentUiToolCall BuildOnboardingDraftCall(IReadOnlyDictionary<string, object?> args)
    {
        var supplierName = ReadString(args, "supplierName");
        var riskTier = ReadString(args, "riskTier");

        return new AgentUiToolCall
        {
            ToolId = AgentUiToolIds.FormDraft,
            Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["blockId"] = "onboarding-draft",
                ["title"] = "Supplier Onboarding Draft",
                ["description"] = "Review details, then apply in chat.",
                ["fields"] = new object?[]
                {
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["name"] = "SupplierName",
                        ["label"] = "Supplier Name",
                        ["type"] = "text",
                        ["value"] = supplierName ?? string.Empty,
                        ["required"] = true
                    },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["name"] = "RiskTier",
                        ["label"] = "Risk Tier",
                        ["type"] = "text",
                        ["value"] = riskTier
                    }
                },
                ["actions"] = new object?[]
                {
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["id"] = "applyOnboardingDraft",
                        ["label"] = "Apply form values",
                        ["prompt"] = "Apply the supplier draft values and confirm."
                    }
                }
            }
        };
    }

    private static AgentUiToolCall BuildOnboardingAppliedCall(IReadOnlyDictionary<string, object?> args)
    {
        var supplierName = ReadString(args, "supplierName");
        var description = string.IsNullOrWhiteSpace(supplierName)
            ? "Supplier draft values were applied in chat."
            : $"Supplier draft values for '{supplierName}' were applied in chat.";

        return new AgentUiToolCall
        {
            ToolId = AgentUiToolIds.ActionConfirmation,
            Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["blockId"] = "onboarding-apply-confirmation",
                ["title"] = "Draft Values Applied",
                ["description"] = description
            }
        };
    }

    private static double ReadRiskScore(IReadOnlyDictionary<string, object?> row)
        => ReadDouble(row, "RiskScore") ?? 0;

    private static IReadOnlyDictionary<string, object?> NormalizeArguments(IReadOnlyDictionary<string, object?> args)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in args)
        {
            normalized[pair.Key] = pair.Value;
        }

        return normalized;
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        var text = raw.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static double? ReadDouble(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            decimal m => (double)m,
            _ when double.TryParse(
                raw.ToString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null
        };
    }
}
