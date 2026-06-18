namespace AgentBlazor.Cli.Analysis.Models;

/// <summary>
/// Root model representing a scanned Blazor project.
/// This is the source of truth - markdown summaries are derived from this.
/// </summary>
public sealed record ProjectModel
{
    public string SchemaVersion { get; init; } = "1";
    public string SolutionPath { get; init; } = "";
    public string AppName { get; init; } = "";
    public string Description { get; init; } = "";
    public IReadOnlyList<string> DesiredAgentWorkflows { get; init; } = [];
    public string BlazorHostProject { get; init; } = "";
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<ProjectNode> Projects { get; init; } = [];
    public IReadOnlyList<RouteModel> Routes { get; init; } = [];
    public IReadOnlyList<ComponentModel> Components { get; init; } = [];
    public IReadOnlyList<ServiceModel> Services { get; init; } = [];
    public IReadOnlyList<ActionModel> Actions { get; init; } = [];
    public IReadOnlyList<WorkflowClusterModel> WorkflowClusters { get; init; } = [];
    public AnalysisCorpus Corpus { get; init; } = new();
    public IReadOnlyList<PageModel> Pages { get; init; } = [];
    public IReadOnlyList<DiRegistrationModel> DiRegistrations { get; init; } = [];
    public IReadOnlyList<RecommendationModel> Recommendations { get; init; } = [];
    public CoverageStats? Coverage { get; init; }
    public IReadOnlyDictionary<string, string> FileHashes { get; init; } = new Dictionary<string, string>();
}

public sealed record ProjectNode
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string TargetFramework { get; init; } = "";
    public bool IsBlazorProject { get; init; }
    public bool IsHostProject { get; init; }
    public IReadOnlyList<string> ProjectReferences { get; init; } = [];
}

public sealed record RouteModel
{
    public string Id { get; init; } = "";
    public string Template { get; init; } = "";
    public string ComponentName { get; init; } = "";
    public string ComponentFile { get; init; } = "";
    public IReadOnlyList<RouteParameterModel> Parameters { get; init; } = [];
    public string? Layout { get; init; }
}

public sealed record RouteParameterModel
{
    public string Name { get; init; } = "";
    public string? Constraint { get; init; }
    public bool IsOptional { get; init; }
    public bool IsCatchAll { get; init; }
}

public sealed record ComponentModel
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string? Namespace { get; init; }
    public bool IsPage { get; init; }
    public IReadOnlyList<string> Routes { get; init; } = [];
    public IReadOnlyList<InjectedServiceModel> InjectedServices { get; init; } = [];
    public IReadOnlyList<string> ChildComponents { get; init; } = [];
    public IReadOnlyList<string> Parameters { get; init; } = [];
    public string? Layout { get; init; }
}

public sealed record InjectedServiceModel
{
    public string TypeName { get; init; } = "";
    public string FieldName { get; init; } = "";
}

public sealed record ServiceModel
{
    public string Id { get; init; } = "";
    public string TypeName { get; init; } = "";
    public string? ImplementationType { get; init; }
    public IReadOnlyList<string> ServiceTypes { get; init; } = [];
    public string FilePath { get; init; } = "";
    public string Lifetime { get; init; } = "Scoped"; // Scoped, Transient, Singleton
    public IReadOnlyList<ServiceMethodModel> Methods { get; init; } = [];
}

public sealed record ServiceMethodModel
{
    public string Name { get; init; } = "";
    public string ReturnType { get; init; } = "";
    public bool IsAsync { get; init; }
    public bool IsPublic { get; init; }
    public IReadOnlyList<ParameterModel> Parameters { get; init; } = [];
    public string? XmlDocSummary { get; init; }
}

public sealed record ParameterModel
{
    public string Name { get; init; } = "";
    public string TypeName { get; init; } = "";
    public bool IsOptional { get; init; }
    public string? DefaultValue { get; init; }
    public string? Description { get; init; }
    public bool IsContextBound { get; init; }  // Has [AgentParam(ContextKey = ...)] - auto-injected
    public bool IsRequired { get; init; }       // Has [AgentParam(Required = true)]
    public string? AllowedValues { get; init; } // Has [AgentParam(AllowedValues = "...")]
}

public sealed record ActionModel
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string SourceService { get; init; } = "";
    public string MethodName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public bool IsMutationLikely { get; init; }
    public bool RequiresApproval { get; init; }
    public ActionClassification Classification { get; init; } = ActionClassification.Unknown;
    public double Score { get; init; }
    public IReadOnlyList<ParameterModel> Parameters { get; init; } = [];
    public IReadOnlyList<string> RelevantRoutes { get; init; } = [];
    public string Summary { get; init; } = "";
    public ActionExposureMode ExposureMode { get; init; } = ActionExposureMode.Suggested;
}

public enum ActionClassification
{
    Unknown,
    Query,      // Read-only data retrieval
    Command,    // State mutation
    Workflow,   // Multi-step process
    Export,     // Data export/reporting
    Validation, // Validation/checking
    Infrastructure // Should be ignored
}

public enum ActionExposureMode
{
    Suggested,  // CLI suggests exposing
    Confirmed,  // Developer confirmed
    Ignored     // Developer chose to ignore
}

public sealed record WorkflowClusterModel
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string SourceService { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string Summary { get; init; } = "";
    public double Confidence { get; init; }
    public bool RequiresApproval { get; init; }
    public string Risk { get; init; } = "";
    public string Origin { get; init; } = "";
    public IReadOnlyList<string> DomainTerms { get; init; } = [];
    public IReadOnlyList<string> RelatedServices { get; init; } = [];
    public IReadOnlyList<string> Evidence { get; init; } = [];
    public IReadOnlyList<WorkflowClusterMethodModel> Methods { get; init; } = [];
    public IReadOnlyList<string> RouteHints { get; init; } = [];
}

public sealed record WorkflowClusterMethodModel
{
    public string Service { get; init; } = "";
    public string Method { get; init; } = "";
    public string Role { get; init; } = "";
    public ActionClassification Classification { get; init; }
    public string Risk { get; init; } = "";
    public string Summary { get; init; } = "";
}

public sealed record PageModel
{
    public string Id { get; init; } = "";
    public string Route { get; init; } = "";
    public string ComponentName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public IReadOnlyList<string> VisibleComponents { get; init; } = [];
    public IReadOnlyList<string> InjectedServices { get; init; } = [];
    public IReadOnlyList<string> SuggestedActions { get; init; } = [];
    public IReadOnlyList<string> DetectedUiElements { get; init; } = [];
    public string Summary { get; init; } = "";
}

/// <summary>
/// Represents a service registration found in DI configuration.
/// </summary>
public sealed record DiRegistrationModel
{
    public string ServiceType { get; init; } = "";
    public string ImplementationType { get; init; } = "";
    public ServiceLifetime Lifetime { get; init; } = ServiceLifetime.Scoped;
    public string FilePath { get; init; } = "";
    public int LineNumber { get; init; }
}

public enum ServiceLifetime
{
    Scoped,
    Transient,
    Singleton
}

public sealed record AnalysisCorpus
{
    public IReadOnlyList<AnalysisCorpusChunk> Chunks { get; init; } = [];

    public IReadOnlyList<RouteCorrelationModel> RouteCorrelations { get; init; } = [];

    public IReadOnlyList<string> DomainTerms { get; init; } = [];

    public IReadOnlyList<FileReferenceModel> FileReferences { get; init; } = [];
}

public sealed record AnalysisCorpusChunk
{
    public string Id { get; init; } = "";

    public AnalysisCorpusChunkKind Kind { get; init; }

    public string Title { get; init; } = "";

    public string Text { get; init; } = "";

    public string FilePath { get; init; } = "";

    public int? LineNumber { get; init; }

    public IReadOnlyList<string> RelatedRoutes { get; init; } = [];

    public IReadOnlyList<string> RelatedServices { get; init; } = [];

    public IReadOnlyList<string> RelatedMethods { get; init; } = [];

    public IReadOnlyList<string> DomainTerms { get; init; } = [];
}

public enum AnalysisCorpusChunkKind
{
    Route,
    Page,
    Service,
    Method,
    DiRegistration,
    Capability,
    WorkflowCluster,
    RouteCorrelation
}

public sealed record RouteCorrelationModel
{
    public string Route { get; init; } = "";

    public string ComponentName { get; init; } = "";

    public string FilePath { get; init; } = "";

    public IReadOnlyList<string> Services { get; init; } = [];

    public IReadOnlyList<string> Methods { get; init; } = [];
}

public sealed record FileReferenceModel
{
    public string Path { get; init; } = "";

    public IReadOnlyList<string> Symbols { get; init; } = [];

    public IReadOnlyList<string> Routes { get; init; } = [];
}

/// <summary>
/// Recommendation for improving AGENT.md coverage with attributes.
/// </summary>
public sealed record RecommendationModel
{
    public RecommendationType Type { get; init; }
    public string TargetName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public int LineNumber { get; init; }
    public string Suggestion { get; init; } = "";
    public double Priority { get; init; } // 0-1, higher = more important
}

public enum RecommendationType
{
    AddAgentAction,       // Method should have [AgentAction]
    AddAgentCapability,   // Service should have [AgentCapability]
    AddDescription,       // Action needs description
    AddApprovalTag,       // Mutation needs approval flag
    AddParameterInfo      // Parameter needs [AgentParam]
}

/// <summary>
/// Coverage statistics for the project.
/// </summary>
public sealed record CoverageStats
{
    public int TotalServices { get; init; }
    public int ServicesWithCapability { get; init; }
    public int TotalActions { get; init; }
    public int ConfirmedActions { get; init; }
    public int DiscoveredActions { get; init; }
    public int ActionsWithDescription { get; init; }
    public int MutationsWithApproval { get; init; }
    public int TotalMutations { get; init; }

    public double ServiceCoveragePercent => TotalServices > 0
        ? Math.Round(100.0 * ServicesWithCapability / TotalServices, 1)
        : 0;

    public double ActionCoveragePercent => TotalActions > 0
        ? Math.Round(100.0 * ConfirmedActions / TotalActions, 1)
        : 0;
}
