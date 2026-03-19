namespace AgentBlazor.App;

/// <summary>
/// Discovers semantic capabilities exposed by the host app and executes them on demand.
/// This is the primary authoring surface for app-defined semantic actions.
/// </summary>
public interface IAgentCapabilityRegistry
{
    IReadOnlyList<AgentCapabilityDescriptor> GetCapabilities(IServiceProvider serviceProvider);

    IReadOnlyList<AgentCapabilityActionDescriptor> GetActions(IServiceProvider serviceProvider);

    bool TryGetAction(
        string actionId,
        IServiceProvider serviceProvider,
        out AgentCapabilityActionDescriptor action);

    Task<CapabilityResult> ExecuteAsync(
        string actionId,
        IReadOnlyDictionary<string, object?> arguments,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default);
}
