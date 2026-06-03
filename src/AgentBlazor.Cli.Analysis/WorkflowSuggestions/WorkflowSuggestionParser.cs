using System.Text.Json;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis.WorkflowSuggestions;

public sealed class WorkflowSuggestionParser
{
    public WorkflowSuggestionSet ParseAndValidate(string responseText, ProjectModel model, string modelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responseText);
        ArgumentNullException.ThrowIfNull(model);

        var normalizedJson = ExtractJsonObject(responseText);
        using var document = JsonDocument.Parse(normalizedJson);
        if (!document.RootElement.TryGetProperty("workflows", out var workflowsElement) ||
            workflowsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("LLM workflow suggestion response did not contain a 'workflows' array.");
        }

        var knownMethods = model.Actions
            .Where(action => action.ExposureMode is ActionExposureMode.Suggested or ActionExposureMode.Confirmed)
            .Where(AnalysisModelFilters.IsDeveloperFacingAction)
            .Select(action => (Service: action.SourceService, Method: NormalizeMethodReference(action.MethodName)))
            .ToHashSet();

        var suggestions = new List<WorkflowSuggestion>();
        var rejected = new List<RejectedWorkflowSuggestion>();

        foreach (var workflowElement in workflowsElement.EnumerateArray())
        {
            var suggestion = ParseSuggestion(workflowElement);
            var invalidMethods = suggestion.Methods
                .Where(method => !knownMethods.Contains((method.Service, method.Method)))
                .ToList();

            if (string.IsNullOrWhiteSpace(suggestion.Name))
            {
                rejected.Add(new RejectedWorkflowSuggestion
                {
                    Name = "(unnamed workflow)",
                    Reason = "Missing workflow name."
                });
                continue;
            }

            if (suggestion.Methods.Count == 0)
            {
                rejected.Add(new RejectedWorkflowSuggestion
                {
                    Name = suggestion.Name,
                    Reason = "No existing methods were referenced."
                });
                continue;
            }

            if (invalidMethods.Count > 0)
            {
                rejected.Add(new RejectedWorkflowSuggestion
                {
                    Name = suggestion.Name,
                    Reason = "Referenced unknown methods: " + string.Join(", ", invalidMethods.Select(method => $"{method.Service}.{method.Method}"))
                });
                continue;
            }

            if (ReferencesOnlyAlreadyExposedMethods(suggestion, model))
            {
                rejected.Add(new RejectedWorkflowSuggestion
                {
                    Name = suggestion.Name,
                    Reason = "All referenced methods already have confirmed AgentBlazor actions."
                });
                continue;
            }

            var semanticallyMisalignedMethods = FindSemanticallyMisalignedMethods(suggestion);
            if (semanticallyMisalignedMethods.Count > 0)
            {
                rejected.Add(new RejectedWorkflowSuggestion
                {
                    Name = suggestion.Name,
                    Reason = "Referenced methods do not align with the workflow description: " +
                        string.Join(", ", semanticallyMisalignedMethods.Select(method => $"{method.Service}.{method.Method}"))
                });
                continue;
            }

            suggestions.Add(SanitizeSuggestionCode(suggestion));
        }

        return new WorkflowSuggestionSet
        {
            Suggestions = suggestions,
            Rejected = rejected,
            Model = modelName
        };
    }

    private static WorkflowSuggestion ParseSuggestion(JsonElement element)
    {
        return new WorkflowSuggestion
        {
            Name = ReadString(element, "name"),
            Description = ReadString(element, "description"),
            CapabilityClass = ReadString(element, "capabilityClass"),
            Code = ReadString(element, "code"),
            Reasoning = ReadString(element, "reasoning"),
            Confidence = ReadDouble(element, "confidence"),
            Methods = ReadMethods(element)
        };
    }

    private static WorkflowSuggestion SanitizeSuggestionCode(WorkflowSuggestion suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion.Code))
        {
            return suggestion;
        }

        return suggestion.Code.Contains("CapabilityResult", StringComparison.Ordinal)
            ? suggestion
            : suggestion with { Code = string.Empty };
    }

    private static bool ReferencesOnlyAlreadyExposedMethods(WorkflowSuggestion suggestion, ProjectModel model)
    {
        var confirmedMethodNames = model.Actions
            .Where(action => action.ExposureMode == ActionExposureMode.Confirmed)
            .Where(AnalysisModelFilters.IsDeveloperFacingAction)
            .Select(action => NormalizeName(action.MethodName))
            .ToHashSet(StringComparer.Ordinal);

        return confirmedMethodNames.Count > 0 &&
            suggestion.Methods.Count > 0 &&
            suggestion.Methods.All(method => confirmedMethodNames.Contains(NormalizeName(method.Method)));
    }

    private static IReadOnlyList<WorkflowMethodReference> FindSemanticallyMisalignedMethods(WorkflowSuggestion suggestion)
    {
        var workflowText = NormalizeText(string.Join(
            ' ',
            suggestion.Name,
            suggestion.Description,
            suggestion.CapabilityClass,
            suggestion.Reasoning));

        return suggestion.Methods
            .Where(method =>
            {
                var methodTokens = ExtractMeaningfulMethodTokens(method.Method);
                var requiredMatches = Math.Min(2, methodTokens.Count);
                return methodTokens.Count > 0 &&
                    methodTokens.Count(token => workflowText.Contains(token, StringComparison.Ordinal)) < requiredMatches;
            })
            .ToList();
    }

    private static IReadOnlyList<string> ExtractMeaningfulMethodTokens(string methodName)
    {
        var normalized = methodName.EndsWith("Async", StringComparison.OrdinalIgnoreCase)
            ? methodName[..^"Async".Length]
            : methodName;

        var words = SplitIdentifier(normalized)
            .Where(word => !IgnoredMethodWords.Contains(word))
            .Where(word => word.Length >= 4)
            .ToList();

        return words.Count == 0
            ? []
            : words;
    }

    private static IReadOnlyList<string> SplitIdentifier(string value)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                Flush();
                continue;
            }

            if (current.Length > 0 && char.IsUpper(character) && !char.IsUpper(current[current.Length - 1]))
            {
                Flush();
            }

            current.Append(char.ToLowerInvariant(character));
        }

        Flush();
        return words;

        void Flush()
        {
            if (current.Length == 0)
            {
                return;
            }

            words.Add(current.ToString());
            current.Clear();
        }
    }

    private static string NormalizeText(string value)
        => new(value
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ')
            .ToArray());

    private static readonly HashSet<string> IgnoredMethodWords = new(StringComparer.Ordinal)
    {
        "add",
        "by",
        "code",
        "create",
        "delete",
        "dispatch",
        "find",
        "get",
        "id",
        "key",
        "list",
        "load",
        "manage",
        "number",
        "query",
        "remove",
        "run",
        "search",
        "set",
        "show",
        "slug",
        "update"
    };

    private static string NormalizeName(string value)
    {
        var normalized = value.EndsWith("Async", StringComparison.OrdinalIgnoreCase)
            ? value[..^"Async".Length]
            : value;

        return new string(normalized
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static IReadOnlyList<WorkflowMethodReference> ReadMethods(JsonElement element)
    {
        if (!element.TryGetProperty("methods", out var methodsElement) ||
            methodsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var methods = new List<WorkflowMethodReference>();
        foreach (var methodElement in methodsElement.EnumerateArray())
        {
            if (methodElement.ValueKind == JsonValueKind.String)
            {
                var methodText = methodElement.GetString() ?? string.Empty;
                var separatorIndex = methodText.LastIndexOf('.');
                if (separatorIndex > 0 && separatorIndex < methodText.Length - 1)
                {
                    methods.Add(new WorkflowMethodReference
                    {
                        Service = methodText[..separatorIndex].Trim(),
                        Method = NormalizeMethodReference(methodText[(separatorIndex + 1)..])
                    });
                }

                continue;
            }

            if (methodElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            methods.Add(new WorkflowMethodReference
            {
                Service = ReadString(methodElement, "service"),
                Method = NormalizeMethodReference(ReadString(methodElement, "method"))
            });
        }

        return methods
            .Where(method => !string.IsNullOrWhiteSpace(method.Service) && !string.IsNullOrWhiteSpace(method.Method))
            .Distinct()
            .ToList();
    }

    private static string NormalizeMethodReference(string method)
    {
        var normalized = method.Trim();
        var parameterStart = normalized.IndexOf('(');
        if (parameterStart > 0)
        {
            normalized = normalized[..parameterStart];
        }

        var whitespaceIndex = normalized.IndexOfAny([' ', '\t', '\r', '\n']);
        if (whitespaceIndex > 0)
        {
            normalized = normalized[..whitespaceIndex];
        }

        return normalized.Trim();
    }

    private static string ExtractJsonObject(string responseText)
    {
        var start = responseText.IndexOf('{');
        var end = responseText.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new InvalidOperationException("LLM workflow suggestion response did not contain a JSON object.");
        }

        return responseText[start..(end + 1)];
    }

    private static string ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static double ReadDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDouble(out var value) => Math.Clamp(value, 0, 1),
            JsonValueKind.String when double.TryParse(property.GetString(), out var value) => Math.Clamp(value, 0, 1),
            _ => 0
        };
    }
}
