using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace AgentBlazor.Demo.Services;

internal sealed partial class E2eDeterministicChatClient : IChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _ = options;
        _ = cancellationToken;

        var lastUserPrompt = messages.LastOrDefault(static message => message.Role == ChatRole.User);
        var plannerInput = GetCombinedText(lastUserPrompt);
        var userRequest = ExtractUserRequest(plannerInput);
        var generatedAction = TryExtractGeneratedUiAction(plannerInput);

        var plan = BuildPlan(userRequest, generatedAction);
        var json = JsonSerializer.Serialize(plan, JsonOptions);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text ?? string.Empty);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        _ = serviceType;
        _ = serviceKey;
        return null;
    }

    public void Dispose()
    {
    }

    private static object BuildPlan(string userRequest, GeneratedUiActionRequest? generatedAction)
    {
        if (generatedAction is not null)
        {
            return BuildGeneratedActionPlan(generatedAction);
        }

        if (ContainsAny(userRequest, "highest risk supplier", "highest-risk supplier", "high risk supplier"))
        {
            return HighestRiskPlan();
        }

        if (ContainsAny(userRequest, "onboarding draft", "create onboarding draft"))
        {
            return OnboardingDraftPlan(ExtractSupplierName(userRequest) ?? "Ash");
        }

        if (ContainsAny(userRequest, "risk forecast", "forecast chart", "risk chart", "risk trend"))
        {
            return RiskForecastPlan(ExtractSupplierName(userRequest) ?? "Ash");
        }

        return EmptyPlan();
    }

    private static object BuildGeneratedActionPlan(GeneratedUiActionRequest action)
    {
        if (string.Equals(action.ActionId, "applyOnboardingDraft", StringComparison.OrdinalIgnoreCase))
        {
            var supplierName = ReadString(action.Payload, "SupplierName")
                ?? ReadString(action.Payload, "supplierName")
                ?? "Ash";

            return new
            {
                reasoning = "Apply onboarding payload in chat.",
                steps = Array.Empty<object>(),
                needsClarification = false,
                confidence = 1.0,
                uiToolCalls = new object[]
                {
                    new
                    {
                        toolId = "onboarding.applied",
                        arguments = new { supplierName }
                    }
                }
            };
        }

        if (string.Equals(action.ActionId, "refreshHighestRisk", StringComparison.OrdinalIgnoreCase))
        {
            return HighestRiskPlan();
        }

        if (string.Equals(action.ActionId, "showOnlyHighRisk", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                reasoning = "Show high-risk suppliers in chat.",
                steps = Array.Empty<object>(),
                needsClarification = false,
                confidence = 1.0,
                uiToolCalls = new object[]
                {
                    new
                    {
                        toolId = "suppliers.highest_risk_focus",
                        arguments = new { mode = "highOnly" }
                    }
                }
            };
        }

        return EmptyPlan();
    }

    private static object HighestRiskPlan()
    {
        return new
        {
            reasoning = "Render supplier risk snapshot in chat.",
            steps = Array.Empty<object>(),
            needsClarification = false,
            confidence = 1.0,
            uiToolCalls = new object[]
            {
                new
                {
                    toolId = "suppliers.highest_risk_focus",
                    arguments = new { }
                }
            }
        };
    }

    private static object OnboardingDraftPlan(string supplierName)
    {
        return new
        {
            reasoning = "Return onboarding draft generated UI.",
            steps = Array.Empty<object>(),
            needsClarification = false,
            confidence = 1.0,
            uiToolCalls = new object[]
            {
                new
                {
                    toolId = "onboarding.draft",
                    arguments = new
                    {
                        supplierName,
                        riskTier = "High"
                    }
                }
            }
        };
    }

    private static object RiskForecastPlan(string supplierName)
    {
        var startMonth = new DateTime(2026, 3, 1);
        var rows = Enumerable.Range(0, 6)
            .Select(index => new
            {
                Month = startMonth.AddMonths(index).ToString("yyyy-MM"),
                ForecastRisk = Math.Min(100, 72 + (index * 3)),
                Note = index < 2 ? "Watchlist" : "Escalate mitigation"
            })
            .ToArray();

        return new
        {
            reasoning = "Render six-month risk forecast in chat.",
            steps = Array.Empty<object>(),
            needsClarification = false,
            confidence = 1.0,
            uiToolCalls = new object[]
            {
                new
                {
                    toolId = "summary.card",
                    arguments = new
                    {
                        blockId = "risk-forecast-summary",
                        title = "Supplier Risk Forecast",
                        description = $"Generated a six-month risk forecast for supplier {supplierName}."
                    }
                },
                new
                {
                    toolId = "table.view",
                    arguments = new
                    {
                        blockId = "risk-forecast-table",
                        title = $"{supplierName} - 6 Month Forecast",
                        description = "Projected risk trajectory by month.",
                        columns = new object[]
                        {
                            new { key = "Month", header = "Month" },
                            new { key = "ForecastRisk", header = "Forecast Risk" },
                            new { key = "Note", header = "Note" }
                        },
                        rows
                    }
                }
            }
        };
    }

    private static object EmptyPlan()
    {
        return new
        {
            reasoning = "No deterministic action mapped.",
            steps = Array.Empty<object>(),
            needsClarification = false,
            confidence = 1.0,
            uiToolCalls = Array.Empty<object>()
        };
    }

    private static bool ContainsAny(string source, params string[] values)
    {
        foreach (var value in values)
        {
            if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetCombinedText(ChatMessage? message)
    {
        if (message is null)
        {
            return string.Empty;
        }

        var contentText = string.Concat(message.Contents
            .OfType<TextContent>()
            .Select(static content => content.Text ?? string.Empty));
        if (!string.IsNullOrWhiteSpace(contentText))
        {
            return contentText;
        }

        return message.Text ?? string.Empty;
    }

    private static string ExtractUserRequest(string plannerInput)
    {
        if (string.IsNullOrWhiteSpace(plannerInput))
        {
            return string.Empty;
        }

        var marker = "USER REQUEST:";
        var index = plannerInput.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return plannerInput.Trim();
        }

        var start = index + marker.Length;
        var end = plannerInput.IndexOf('\n', start);
        if (end < 0)
        {
            return plannerInput[start..].Trim();
        }

        return plannerInput[start..end].Trim();
    }

    private static GeneratedUiActionRequest? TryExtractGeneratedUiAction(string plannerInput)
    {
        var marker = "GENERATED_UI_ACTION_JSON:";
        var index = plannerInput.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var json = plannerInput[(index + marker.Length)..].Trim();
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var blockId = TryGetString(root, "blockId");
            var actionId = TryGetString(root, "actionId");
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return null;
            }

            var payload = root.TryGetProperty("payload", out var payloadElement) && payloadElement.ValueKind == JsonValueKind.Object
                ? ConvertObject(payloadElement)
                : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            return new GeneratedUiActionRequest(
                blockId ?? string.Empty,
                actionId,
                payload);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractSupplierName(string request)
    {
        var match = SupplierNameRegex().Match(request);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups["name"].Value.Trim();
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        var value = raw.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : element.ToString();
    }

    private static Dictionary<string, object?> ConvertObject(JsonElement element)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = ConvertElement(property.Value);
        }

        return result;
    }

    private static object? ConvertElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var i64) => i64,
            JsonValueKind.Number when element.TryGetDouble(out var d) => d,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => ConvertObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertElement).ToArray(),
            _ => null
        };
    }

    [GeneratedRegex(@"\bsupplier\s+(?<name>[A-Za-z0-9\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SupplierNameRegex();

    private sealed record GeneratedUiActionRequest(
        string BlockId,
        string ActionId,
        IReadOnlyDictionary<string, object?> Payload);
}
