using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis.Blazor;

public sealed record BlazorFrameworkContext
{
    public string HostProjectName { get; init; } = string.Empty;

    public string HostProjectPath { get; init; } = string.Empty;

    public string? UiProjectName { get; init; }

    public string? UiProjectPath { get; init; }

    public HostShapeAssessment? HostShape { get; init; }

    public bool HasAgentBlazorServices { get; init; }

    public bool HasWorkflowRegistration { get; init; }

    public bool HasEndpointMapping { get; init; }

    public bool HasChatSurface { get; init; }
}
