using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

// ActionScorer with config support

/// <summary>
/// Scores and classifies service methods as potential agent actions.
/// Uses domain verb heuristics to identify good candidates.
/// </summary>
public sealed class ActionScorer
{
    // Additional verbs from config
    private HashSet<string>? _additionalVerbs;
    private HashSet<string>? _excludePatterns;

    /// <summary>
    /// Configures the scorer with user-defined patterns from config file.
    /// </summary>
    public void Configure(AgentBlazorConfig config)
    {
        if (config.AdditionalDomainVerbs?.Count > 0)
        {
            _additionalVerbs = new HashSet<string>(config.AdditionalDomainVerbs, StringComparer.OrdinalIgnoreCase);
        }
        if (config.ExcludeMethodPatterns?.Count > 0)
        {
            _excludePatterns = new HashSet<string>(config.ExcludeMethodPatterns, StringComparer.OrdinalIgnoreCase);
        }
    }

    // Domain verbs that indicate good action candidates
    private static readonly Dictionary<string, (ActionClassification Classification, bool IsMutation)> DomainVerbs =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // Query/Read operations
        ["Get"] = (ActionClassification.Query, false),
        ["Find"] = (ActionClassification.Query, false),
        ["Search"] = (ActionClassification.Query, false),
        ["List"] = (ActionClassification.Query, false),
        ["Fetch"] = (ActionClassification.Query, false),
        ["Load"] = (ActionClassification.Query, false),
        ["Query"] = (ActionClassification.Query, false),
        ["Lookup"] = (ActionClassification.Query, false),
        ["Retrieve"] = (ActionClassification.Query, false),
        ["Read"] = (ActionClassification.Query, false),
        ["Count"] = (ActionClassification.Query, false),
        ["Exists"] = (ActionClassification.Query, false),

        // Command/Mutation operations
        ["Create"] = (ActionClassification.Command, true),
        ["Add"] = (ActionClassification.Command, true),
        ["Insert"] = (ActionClassification.Command, true),
        ["Update"] = (ActionClassification.Command, true),
        ["Modify"] = (ActionClassification.Command, true),
        ["Edit"] = (ActionClassification.Command, true),
        ["Change"] = (ActionClassification.Command, true),
        ["Set"] = (ActionClassification.Command, true),
        ["Delete"] = (ActionClassification.Command, true),
        ["Remove"] = (ActionClassification.Command, true),
        ["Clear"] = (ActionClassification.Command, true),
        ["Save"] = (ActionClassification.Command, true),
        ["Store"] = (ActionClassification.Command, true),
        ["Persist"] = (ActionClassification.Command, true),
        ["Put"] = (ActionClassification.Command, true),
        ["Patch"] = (ActionClassification.Command, true),

        // Toggle/State operations
        ["Toggle"] = (ActionClassification.Command, true),
        ["Enable"] = (ActionClassification.Command, true),
        ["Disable"] = (ActionClassification.Command, true),
        ["Activate"] = (ActionClassification.Command, true),
        ["Deactivate"] = (ActionClassification.Command, true),
        ["Lock"] = (ActionClassification.Command, true),
        ["Unlock"] = (ActionClassification.Command, true),
        ["Show"] = (ActionClassification.Command, true),
        ["Hide"] = (ActionClassification.Command, true),
        ["Expand"] = (ActionClassification.Command, true),
        ["Collapse"] = (ActionClassification.Command, true),

        // Workflow operations
        ["Submit"] = (ActionClassification.Workflow, true),
        ["Approve"] = (ActionClassification.Workflow, true),
        ["Reject"] = (ActionClassification.Workflow, true),
        ["Review"] = (ActionClassification.Workflow, false),
        ["Prepare"] = (ActionClassification.Workflow, true),
        ["Process"] = (ActionClassification.Workflow, true),
        ["Execute"] = (ActionClassification.Workflow, true),
        ["Run"] = (ActionClassification.Workflow, true),
        ["Start"] = (ActionClassification.Workflow, true),
        ["Stop"] = (ActionClassification.Workflow, true),
        ["Pause"] = (ActionClassification.Workflow, true),
        ["Resume"] = (ActionClassification.Workflow, true),
        ["Complete"] = (ActionClassification.Workflow, true),
        ["Finalize"] = (ActionClassification.Workflow, true),
        ["Cancel"] = (ActionClassification.Workflow, true),
        ["Assign"] = (ActionClassification.Workflow, true),
        ["Unassign"] = (ActionClassification.Workflow, true),
        ["Escalate"] = (ActionClassification.Workflow, true),
        ["Resolve"] = (ActionClassification.Workflow, true),
        ["Close"] = (ActionClassification.Workflow, true),
        ["Reopen"] = (ActionClassification.Workflow, true),
        ["Apply"] = (ActionClassification.Workflow, true),
        ["Reset"] = (ActionClassification.Workflow, true),
        ["Retry"] = (ActionClassification.Workflow, true),
        ["Rollback"] = (ActionClassification.Workflow, true),
        ["Undo"] = (ActionClassification.Workflow, true),
        ["Redo"] = (ActionClassification.Workflow, true),

        // Auth operations
        ["Login"] = (ActionClassification.Workflow, true),
        ["Logout"] = (ActionClassification.Workflow, true),
        ["SignIn"] = (ActionClassification.Workflow, true),
        ["SignOut"] = (ActionClassification.Workflow, true),
        ["Register"] = (ActionClassification.Workflow, true),
        ["Authenticate"] = (ActionClassification.Workflow, true),
        ["Authorize"] = (ActionClassification.Workflow, false),

        // Communication operations
        ["Send"] = (ActionClassification.Command, true),
        ["Post"] = (ActionClassification.Command, true),
        ["Publish"] = (ActionClassification.Command, true),
        ["Notify"] = (ActionClassification.Command, true),
        ["Alert"] = (ActionClassification.Command, true),
        ["Email"] = (ActionClassification.Command, true),
        ["Message"] = (ActionClassification.Command, true),
        ["Broadcast"] = (ActionClassification.Command, true),

        // Data transfer operations
        ["Upload"] = (ActionClassification.Command, true),
        ["Import"] = (ActionClassification.Command, true),
        ["Sync"] = (ActionClassification.Command, true),
        ["Refresh"] = (ActionClassification.Command, true),
        ["Pull"] = (ActionClassification.Command, true),
        ["Push"] = (ActionClassification.Command, true),
        ["Clone"] = (ActionClassification.Command, true),
        ["Copy"] = (ActionClassification.Command, true),
        ["Move"] = (ActionClassification.Command, true),
        ["Transfer"] = (ActionClassification.Command, true),

        // Export operations
        ["Export"] = (ActionClassification.Export, false),
        ["Generate"] = (ActionClassification.Export, false),
        ["Download"] = (ActionClassification.Export, false),
        ["Print"] = (ActionClassification.Export, false),
        ["Render"] = (ActionClassification.Export, false),
        ["Build"] = (ActionClassification.Export, false),
        ["Compile"] = (ActionClassification.Export, false),
        ["Format"] = (ActionClassification.Export, false),
        ["Convert"] = (ActionClassification.Export, false),

        // Archive operations
        ["Archive"] = (ActionClassification.Command, true),
        ["Restore"] = (ActionClassification.Command, true),
        ["Backup"] = (ActionClassification.Command, true),
        ["Recover"] = (ActionClassification.Command, true),

        // Scheduling operations
        ["Schedule"] = (ActionClassification.Workflow, true),
        ["Trigger"] = (ActionClassification.Workflow, true),
        ["Queue"] = (ActionClassification.Workflow, true),
        ["Dispatch"] = (ActionClassification.Workflow, true),

        // Validation operations
        ["Validate"] = (ActionClassification.Validation, false),
        ["Check"] = (ActionClassification.Validation, false),
        ["Verify"] = (ActionClassification.Validation, false),
        ["Confirm"] = (ActionClassification.Validation, false),
        ["Test"] = (ActionClassification.Validation, false),
        ["Inspect"] = (ActionClassification.Validation, false),
        ["Audit"] = (ActionClassification.Validation, false),

        // Analysis/Explanation
        ["Explain"] = (ActionClassification.Query, false),
        ["Analyze"] = (ActionClassification.Query, false),
        ["Calculate"] = (ActionClassification.Query, false),
        ["Compare"] = (ActionClassification.Query, false),
        ["Summarize"] = (ActionClassification.Query, false),
        ["Assess"] = (ActionClassification.Query, false),
        ["Evaluate"] = (ActionClassification.Query, false),
        ["Predict"] = (ActionClassification.Query, false),
        ["Recommend"] = (ActionClassification.Query, false),

        // Selection operations
        ["Select"] = (ActionClassification.Command, true),
        ["Deselect"] = (ActionClassification.Command, true),
        ["Pick"] = (ActionClassification.Command, true),
        ["Choose"] = (ActionClassification.Command, true),
        ["Filter"] = (ActionClassification.Query, false),
        ["Sort"] = (ActionClassification.Query, false),
        ["Group"] = (ActionClassification.Query, false),
        ["Order"] = (ActionClassification.Query, false),

        // Navigation (lower priority - often UI-only)
        ["Navigate"] = (ActionClassification.Command, false),
        ["Redirect"] = (ActionClassification.Command, false),
        ["Open"] = (ActionClassification.Command, false),
        ["Focus"] = (ActionClassification.Command, false),
    };

    // Infrastructure methods that should be ignored
    private static readonly HashSet<string> InfrastructureMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        // Object methods
        "Dispose",
        "DisposeAsync",
        "ToString",
        "GetHashCode",
        "Equals",
        "GetType",
        "Clone",
        "MemberwiseClone",

        // EF Core / Database
        "SaveChanges",
        "SaveChangesAsync",
        "BeginTransaction",
        "BeginTransactionAsync",
        "CommitTransaction",
        "CommitTransactionAsync",
        "RollbackTransaction",
        "RollbackTransactionAsync",
        "GetDbConnection",
        "ExecuteSql",
        "ExecuteSqlRaw",
        "ExecuteSqlRawAsync",
        "ExecuteSqlInterpolated",
        "ExecuteSqlInterpolatedAsync",
        "EnsureCreated",
        "EnsureCreatedAsync",
        "EnsureDeleted",
        "EnsureDeletedAsync",
        "Migrate",
        "MigrateAsync",
        "CanConnect",
        "CanConnectAsync",

        // Blazor component lifecycle
        "OnInitialized",
        "OnInitializedAsync",
        "OnParametersSet",
        "OnParametersSetAsync",
        "OnAfterRender",
        "OnAfterRenderAsync",
        "SetParametersAsync",
        "StateHasChanged",
        "ShouldRender",
        "BuildRenderTree",
        "InvokeAsync",

        // ASP.NET Core / DI
        "Configure",
        "ConfigureServices",
        "ConfigureAsync",
        "Initialize",
        "InitializeAsync",
        "AddServices",
        "RegisterServices",

        // Serialization
        "Serialize",
        "SerializeAsync",
        "Deserialize",
        "DeserializeAsync",
        "ToJson",
        "FromJson",
        "ToXml",
        "FromXml",

        // Mapping
        "Map",
        "MapTo",
        "MapFrom",
        "ToDto",
        "FromDto",
        "ToEntity",
        "FromEntity",
        "ToModel",
        "FromModel",

        // Logging/Tracing
        "Log",
        "LogDebug",
        "LogInformation",
        "LogWarning",
        "LogError",
        "LogCritical",
        "Trace",
        "TraceEvent",

        // Caching
        "GetOrAdd",
        "GetOrAddAsync",
        "TryGetValue",
        "Invalidate",
        "InvalidateAsync"
    };

    // Method name patterns that indicate infrastructure/internal methods
    private static readonly string[] InfrastructureMethodPatterns =
    [
        "Handle",      // Event handlers like HandleClick, HandleSubmit
        "Callback",    // Callbacks like OnClickCallback
        "Invoke",      // Invocations
        "RaiseEvent",  // Event raising
        "NotifyPropertyChanged",
        "SetProperty", // Property setters
        "GetProperty", // Property getters
        "Is",          // Boolean checks like IsValid, IsEnabled
        "Has",         // Boolean checks like HasPermission
        "Can",         // Capability checks like CanEdit
        "Try",         // TryParse, TryGet patterns (when no matching verb)
        "Internal",    // Explicitly internal
        "Private",     // Explicitly private helpers
        "Helper",      // Helper methods
        "Util"         // Utility methods
    ];

    // Infrastructure type prefixes that indicate low-level methods
    private static readonly string[] InfrastructureTypePrefixes =
    [
        "System.",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Extensions",
        "DbContext",
        "IDbConnection"
    ];

    public ActionScore ScoreMethod(ServiceMethodModel method, string serviceName)
    {
        var methodName = method.Name;

        // Check user-defined exclusions first
        if (_excludePatterns != null)
        {
            foreach (var pattern in _excludePatterns)
            {
                if (methodName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return new ActionScore
                    {
                        Score = 0,
                        Classification = ActionClassification.Infrastructure,
                        IsMutation = false,
                        Reason = $"Excluded by config pattern: {pattern}"
                    };
                }
            }
        }

        // Immediate disqualification for infrastructure methods
        if (InfrastructureMethods.Contains(methodName))
        {
            return new ActionScore
            {
                Score = 0,
                Classification = ActionClassification.Infrastructure,
                IsMutation = false,
                Reason = "Infrastructure method"
            };
        }

        // Check for Async suffix and strip it for verb detection
        var nameForVerb = methodName;
        if (nameForVerb.EndsWith("Async", StringComparison.Ordinal))
        {
            nameForVerb = nameForVerb[..^5];
        }

        // Check if the method name starts with an infrastructure pattern
        // (but only if it doesn't match a domain verb - domain verbs take precedence)
        var matchesDomainVerb = DomainVerbs.Keys.Any(verb =>
            nameForVerb.StartsWith(verb, StringComparison.OrdinalIgnoreCase));

        if (!matchesDomainVerb)
        {
            foreach (var pattern in InfrastructureMethodPatterns)
            {
                if (nameForVerb.StartsWith(pattern, StringComparison.OrdinalIgnoreCase) ||
                    nameForVerb.EndsWith(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return new ActionScore
                    {
                        Score = 0.1,
                        Classification = ActionClassification.Infrastructure,
                        IsMutation = false,
                        Reason = $"Infrastructure pattern: {pattern}"
                    };
                }
            }
        }

        // Find matching domain verb
        foreach (var (verb, (classification, isMutation)) in DomainVerbs)
        {
            if (nameForVerb.StartsWith(verb, StringComparison.OrdinalIgnoreCase))
            {
                var score = CalculateScore(method, verb, classification);
                return new ActionScore
                {
                    Score = score,
                    Classification = classification,
                    IsMutation = isMutation,
                    MatchedVerb = verb,
                    Reason = $"Matched domain verb: {verb}"
                };
            }
        }

        // Check user-defined additional verbs (treated as workflow/mutation by default)
        if (_additionalVerbs != null)
        {
            foreach (var verb in _additionalVerbs)
            {
                if (nameForVerb.StartsWith(verb, StringComparison.OrdinalIgnoreCase))
                {
                    var score = CalculateScore(method, verb, ActionClassification.Workflow);
                    return new ActionScore
                    {
                        Score = score,
                        Classification = ActionClassification.Workflow,
                        IsMutation = true,
                        MatchedVerb = verb,
                        Reason = $"Matched custom verb: {verb}"
                    };
                }
            }
        }

        // No domain verb match - check if it's still potentially useful
        var fallbackScore = CalculateFallbackScore(method);
        return new ActionScore
        {
            Score = fallbackScore,
            Classification = ActionClassification.Unknown,
            IsMutation = false,
            Reason = fallbackScore > 0.3 ? "Public async method" : "Low relevance"
        };
    }

    private double CalculateScore(ServiceMethodModel method, string verb, ActionClassification classification)
    {
        var score = 0.5; // Base score for verb match

        // Async methods are preferred
        if (method.IsAsync)
            score += 0.15;

        // Methods with meaningful parameters are preferred
        var meaningfulParams = method.Parameters
            .Where(p => !p.TypeName.Contains("CancellationToken"))
            .ToList();

        if (meaningfulParams.Count > 0 && meaningfulParams.Count <= 5)
            score += 0.1;

        // Domain-specific parameter types are preferred (DTOs, IDs, filters)
        foreach (var param in meaningfulParams)
        {
            if (param.TypeName.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ||
                param.TypeName == "Guid" ||
                param.TypeName == "int" ||
                param.TypeName == "string")
            {
                score += 0.05;
            }
            else if (param.TypeName.Contains("[]") || param.TypeName.StartsWith("IEnumerable"))
            {
                score += 0.05; // Batch operations
            }
            else if (param.TypeName.Contains("Dto") ||
                     param.TypeName.Contains("Request") ||
                     param.TypeName.Contains("Command") ||
                     param.TypeName.Contains("Query") ||
                     param.TypeName.Contains("Filter") ||
                     param.TypeName.Contains("Input") ||
                     param.TypeName.Contains("Args") ||
                     param.TypeName.Contains("Options") ||
                     param.TypeName.Contains("Model") ||
                     param.TypeName.Contains("Payload"))
            {
                score += 0.1; // Domain DTOs
            }
        }

        // Workflow and mutation operations get a boost
        if (classification == ActionClassification.Workflow)
            score += 0.1;

        // Export operations are high value for agents
        if (classification == ActionClassification.Export)
            score += 0.1;

        // Has XML documentation
        if (!string.IsNullOrEmpty(method.XmlDocSummary))
            score += 0.05;

        // Methods returning result types are useful
        if (method.ReturnType.Contains("Result") ||
            method.ReturnType.Contains("Response") ||
            method.ReturnType.Contains("Output"))
        {
            score += 0.05;
        }

        return Math.Min(1.0, score);
    }

    private double CalculateFallbackScore(ServiceMethodModel method)
    {
        // Non-verb-matched methods start with low score
        var score = 0.2;

        // Async public methods are more likely to be useful
        if (method.IsAsync)
            score += 0.1;

        // Task-returning methods are candidates
        if (method.ReturnType.StartsWith("Task<") || method.ReturnType.StartsWith("ValueTask<"))
            score += 0.05;

        // Methods returning void or Task (not Task<T>) are often side-effects
        if (method.ReturnType == "void" || method.ReturnType == "Task")
            score -= 0.1;

        return Math.Max(0, score);
    }

    /// <summary>
    /// Converts a scored method into an ActionModel.
    /// </summary>
    public ActionModel ToActionModel(
        ServiceMethodModel method,
        ServiceModel service,
        ActionScore score,
        double scoreThreshold = 0.4)
    {
        var shouldExpose = score.Score >= scoreThreshold &&
                          score.Classification != ActionClassification.Infrastructure;

        // Generate a human-readable name
        var actionName = GenerateActionName(method.Name, service.TypeName);

        return new ActionModel
        {
            Id = GenerateActionId(service.TypeName, method.Name),
            Name = actionName,
            SourceService = service.TypeName,
            MethodName = method.Name,
            FilePath = service.FilePath,
            IsMutationLikely = score.IsMutation,
            RequiresApproval = score.IsMutation && score.Classification == ActionClassification.Workflow,
            Classification = score.Classification,
            Score = score.Score,
            Parameters = method.Parameters,
            Summary = method.XmlDocSummary ?? GenerateSummary(method.Name, score),
            ExposureMode = shouldExpose ? ActionExposureMode.Suggested : ActionExposureMode.Ignored
        };
    }

    private static string GenerateActionId(string serviceName, string methodName)
    {
        // Remove common suffixes
        var cleanService = serviceName
            .Replace("Service", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Capabilities", "", StringComparison.OrdinalIgnoreCase);

        var cleanMethod = methodName;
        if (cleanMethod.EndsWith("Async", StringComparison.Ordinal))
            cleanMethod = cleanMethod[..^5];

        return ToSnakeCase(cleanService) + "." + ToSnakeCase(cleanMethod);
    }

    private static string GenerateActionName(string methodName, string serviceName)
    {
        // Strip Async suffix
        var name = methodName;
        if (name.EndsWith("Async", StringComparison.Ordinal))
            name = name[..^5];

        // Convert to title case with spaces
        return string.Concat(name.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
    }

    private static string GenerateSummary(string methodName, ActionScore score)
    {
        var verb = score.MatchedVerb ?? "Perform";
        var action = methodName;
        if (action.EndsWith("Async", StringComparison.Ordinal))
            action = action[..^5];

        // Strip the verb from the method name to get the subject
        if (score.MatchedVerb != null &&
            action.StartsWith(score.MatchedVerb, StringComparison.OrdinalIgnoreCase))
        {
            action = action[score.MatchedVerb.Length..];
        }

        // Convert to readable form
        var subject = string.Concat(action.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? " " + char.ToLower(c) : char.ToLower(c).ToString()));

        return $"{verb} {subject.Trim()}.";
    }

    private static string ToSnakeCase(string input)
    {
        return string.Concat(input.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }
}

public sealed class ActionScore
{
    public double Score { get; init; }
    public ActionClassification Classification { get; init; }
    public bool IsMutation { get; init; }
    public string? MatchedVerb { get; init; }
    public string Reason { get; init; } = "";
}
