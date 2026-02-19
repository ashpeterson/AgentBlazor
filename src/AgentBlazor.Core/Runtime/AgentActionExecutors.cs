using AgentBlazor.Components;

namespace AgentBlazor.Runtime;

public sealed record DataGridActionRequest(
    string ActionId,
    IReadOnlyDictionary<string, object?>? Arguments = null);

public sealed record DialogActionRequest(
    string ActionId,
    IReadOnlyDictionary<string, object?>? Arguments = null);

public sealed record FormActionRequest(
    string ActionId,
    IReadOnlyDictionary<string, object?>? Arguments = null);

public sealed record NavigationActionRequest(
    string ActionId,
    IReadOnlyDictionary<string, object?>? Arguments = null);

public sealed record TabsActionRequest(
    string ActionId,
    IReadOnlyDictionary<string, object?>? Arguments = null);

public interface IDataGridActionExecutor
{
    Task<ComponentActionExecutionResult> ExecuteAsync(
        DataGridActionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IDialogActionExecutor
{
    Task<ComponentActionExecutionResult> ExecuteAsync(
        DialogActionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IFormActionExecutor
{
    Task<ComponentActionExecutionResult> ExecuteAsync(
        FormActionRequest request,
        CancellationToken cancellationToken = default);
}

public interface INavigationActionExecutor
{
    Task<ComponentActionExecutionResult> ExecuteAsync(
        NavigationActionRequest request,
        CancellationToken cancellationToken = default);
}

public interface ITabsActionExecutor
{
    Task<ComponentActionExecutionResult> ExecuteAsync(
        TabsActionRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class NoOpDataGridActionExecutor : IDataGridActionExecutor
{
    public Task<ComponentActionExecutionResult> ExecuteAsync(
        DataGridActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ComponentActionExecutionResult(
            AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
            request.ActionId,
            Succeeded: true,
            Message: $"Simulated AgentDataGrid action: {request.ActionId}."));
    }
}

internal sealed class NoOpDialogActionExecutor : IDialogActionExecutor
{
    public Task<ComponentActionExecutionResult> ExecuteAsync(
        DialogActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ComponentActionExecutionResult(
            AgentComponentV1CapabilityProfile.AgentDialogComponentId,
            request.ActionId,
            Succeeded: true,
            Message: $"Simulated AgentDialog action: {request.ActionId}."));
    }
}

internal sealed class NoOpFormActionExecutor : IFormActionExecutor
{
    public Task<ComponentActionExecutionResult> ExecuteAsync(
        FormActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ComponentActionExecutionResult(
            AgentComponentV1CapabilityProfile.AgentFormComponentId,
            request.ActionId,
            Succeeded: true,
            Message: $"Simulated AgentForm action: {request.ActionId}."));
    }
}

internal sealed class NoOpNavigationActionExecutor : INavigationActionExecutor
{
    public Task<ComponentActionExecutionResult> ExecuteAsync(
        NavigationActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ComponentActionExecutionResult(
            AgentComponentV1CapabilityProfile.AgentNavMenuComponentId,
            request.ActionId,
            Succeeded: true,
            Message: $"Simulated AgentNavMenu action: {request.ActionId}."));
    }
}

internal sealed class NoOpTabsActionExecutor : ITabsActionExecutor
{
    public Task<ComponentActionExecutionResult> ExecuteAsync(
        TabsActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ComponentActionExecutionResult(
            AgentComponentV1CapabilityProfile.AgentTabsComponentId,
            request.ActionId,
            Succeeded: true,
            Message: $"Simulated AgentTabs action: {request.ActionId}."));
    }
}
