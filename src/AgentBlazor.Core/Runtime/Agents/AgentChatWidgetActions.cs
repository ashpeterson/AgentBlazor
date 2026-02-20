using AgentBlazor.Core.Runtime.Components;

namespace AgentBlazor.Core.Runtime.Agents;

public sealed record ChatWidgetActionRequest(
    string ActionId,
    IReadOnlyDictionary<string, object?>? Arguments = null);

public interface IChatWidgetActionExecutor
{
    Task<ComponentActionExecutionResult> ExecuteAsync(
        ChatWidgetActionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAgentChatWidgetState
{
    bool IsOpen { get; }

    event Action? Changed;

    void Open();

    void Close();

    void Toggle();
}

internal sealed class AgentChatWidgetState : IAgentChatWidgetState
{
    private bool _isOpen;

    public bool IsOpen => _isOpen;

    public event Action? Changed;

    public void Open()
    {
        if (_isOpen)
        {
            return;
        }

        _isOpen = true;
        Changed?.Invoke();
    }

    public void Close()
    {
        if (!_isOpen)
        {
            return;
        }

        _isOpen = false;
        Changed?.Invoke();
    }

    public void Toggle()
    {
        if (_isOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }
}

internal sealed class NoOpChatWidgetActionExecutor(
    IAgentChatWidgetState chatWidgetState) : IChatWidgetActionExecutor
{
    public Task<ComponentActionExecutionResult> ExecuteAsync(
        ChatWidgetActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var actionId = request.ActionId.Trim();
        if (string.Equals(actionId, "open_widget", StringComparison.OrdinalIgnoreCase))
        {
            chatWidgetState.Open();
            return Task.FromResult(new ComponentActionExecutionResult(
                ComponentId: "AgentChatWidget",
                ActionId: actionId,
                Succeeded: true,
                Message: "Opened chat widget."));
        }

        if (string.Equals(actionId, "close_widget", StringComparison.OrdinalIgnoreCase))
        {
            chatWidgetState.Close();
            return Task.FromResult(new ComponentActionExecutionResult(
                ComponentId: "AgentChatWidget",
                ActionId: actionId,
                Succeeded: true,
                Message: "Closed chat widget."));
        }

        return Task.FromResult(new ComponentActionExecutionResult(
            ComponentId: "AgentChatWidget",
            ActionId: actionId,
            Succeeded: false,
            Message: $"AgentChatWidget action '{actionId}' is not supported."));
    }
}
