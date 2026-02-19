using AgentBlazor.Components;
using AgentBlazor.Runtime;

namespace AgentBlazor.Demo.Services;

public sealed class AgentDialogFormDemoState
{
    private readonly object _gate = new();
    private readonly Queue<string> _recentEvents = new();
    private readonly List<SupplierDraftPreset> _presets =
    [
        new("Contoso Retail UK", "ops@contoso-retail.co.uk", "High", 85000m),
        new("Northstar Foods", "supply@northstarfoods.com", "Medium", 42000m),
        new("Aquila MedSystems", "procurement@aquila-med.com", "Critical", 130000m),
        new("Harbor Battery Labs", "compliance@harborbattery.io", "Low", 21000m)
    ];

    private int _presetIndex;
    private bool _dialogVisible;
    private SupplierOnboardingDraft _draft = new();
    private bool _lastValidationSucceeded;
    private string[] _validationErrors = [];
    private int _submissionCount;
    private DateTimeOffset? _lastSubmittedUtc;

    public event Action? Changed;

    public AgentDialogFormDemoSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return BuildSnapshotUnsafe();
            }
        }
    }

    public ComponentActionExecutionResult ApplyDialogAction(string actionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        bool succeeded;
        string message;
        lock (_gate)
        {
            (succeeded, message) = actionId.Trim().ToLowerInvariant() switch
            {
                AgentComponentV1CapabilityProfile.DialogOpenActionId => OpenDialogUnsafe(),
                AgentComponentV1CapabilityProfile.DialogCloseActionId => CloseDialogUnsafe(),
                AgentComponentV1CapabilityProfile.DialogConfirmActionId => ConfirmDialogUnsafe(),
                _ => (
                    false,
                    $"AgentDialog action '{actionId}' is not implemented by demo state.")
            };

            EnqueueEventUnsafe($"{DateTime.UtcNow:HH:mm:ss} - {message}");
        }

        Changed?.Invoke();
        return new ComponentActionExecutionResult(
            ComponentId: AgentComponentV1CapabilityProfile.AgentDialogComponentId,
            ActionId: actionId,
            Succeeded: succeeded,
            Message: message);
    }

    public ComponentActionExecutionResult ApplyFormAction(string actionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        bool succeeded;
        string message;
        lock (_gate)
        {
            (succeeded, message) = actionId.Trim().ToLowerInvariant() switch
            {
                AgentComponentV1CapabilityProfile.FormSetFieldActionId => SetNextPresetUnsafe(),
                AgentComponentV1CapabilityProfile.FormValidateActionId => ValidateUnsafe(),
                AgentComponentV1CapabilityProfile.FormResetActionId => ResetUnsafe(),
                AgentComponentV1CapabilityProfile.FormSubmitActionId => SubmitUnsafe(),
                _ => (
                    false,
                    $"AgentForm action '{actionId}' is not implemented by demo state.")
            };

            EnqueueEventUnsafe($"{DateTime.UtcNow:HH:mm:ss} - {message}");
        }

        Changed?.Invoke();
        return new ComponentActionExecutionResult(
            ComponentId: AgentComponentV1CapabilityProfile.AgentFormComponentId,
            ActionId: actionId,
            Succeeded: succeeded,
            Message: message);
    }

    private (bool Succeeded, string Message) OpenDialogUnsafe()
    {
        _dialogVisible = true;
        return (true, "Opened supplier onboarding dialog.");
    }

    private (bool Succeeded, string Message) CloseDialogUnsafe()
    {
        _dialogVisible = false;
        return (true, "Closed supplier onboarding dialog.");
    }

    private (bool Succeeded, string Message) ConfirmDialogUnsafe()
    {
        return SubmitUnsafe();
    }

    private (bool Succeeded, string Message) SetNextPresetUnsafe()
    {
        var preset = _presets[_presetIndex];
        _presetIndex = (_presetIndex + 1) % _presets.Count;

        _draft = new SupplierOnboardingDraft
        {
            SupplierName = preset.SupplierName,
            ContactEmail = preset.ContactEmail,
            RiskTier = preset.RiskTier,
            RequestedBudget = preset.RequestedBudget
        };
        _dialogVisible = true;
        _lastValidationSucceeded = false;
        _validationErrors = [];
        return (true, $"Set AgentForm fields using preset '{preset.SupplierName}'.");
    }

    private (bool Succeeded, string Message) ValidateUnsafe()
    {
        _validationErrors = BuildValidationErrorsUnsafe(_draft).ToArray();
        _lastValidationSucceeded = _validationErrors.Length == 0;
        var message = _lastValidationSucceeded
            ? "Validated supplier onboarding form successfully."
            : $"Validation failed with {_validationErrors.Length} error(s).";
        return (_lastValidationSucceeded, message);
    }

    private (bool Succeeded, string Message) SubmitUnsafe()
    {
        var (isValid, validationMessage) = ValidateUnsafe();
        if (!isValid)
        {
            return (false, $"Submit blocked. {validationMessage}");
        }

        _submissionCount++;
        _lastSubmittedUtc = DateTimeOffset.UtcNow;
        _dialogVisible = false;
        return (true, $"Submitted supplier onboarding form for '{_draft.SupplierName}'.");
    }

    private (bool Succeeded, string Message) ResetUnsafe()
    {
        _draft = new SupplierOnboardingDraft();
        _validationErrors = [];
        _lastValidationSucceeded = false;
        return (true, "Reset supplier onboarding form draft.");
    }

    private static IEnumerable<string> BuildValidationErrorsUnsafe(SupplierOnboardingDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.SupplierName))
        {
            yield return "Supplier name is required.";
        }

        if (string.IsNullOrWhiteSpace(draft.ContactEmail) || !draft.ContactEmail.Contains('@', StringComparison.Ordinal))
        {
            yield return "Contact email must contain '@'.";
        }

        if (string.IsNullOrWhiteSpace(draft.RiskTier))
        {
            yield return "Risk tier is required.";
        }

        if (draft.RequestedBudget <= 0)
        {
            yield return "Requested budget must be greater than zero.";
        }
    }

    private AgentDialogFormDemoSnapshot BuildSnapshotUnsafe() =>
        new(
            DialogVisible: _dialogVisible,
            Draft: _draft with { },
            LastValidationSucceeded: _lastValidationSucceeded,
            ValidationErrors: _validationErrors.ToArray(),
            SubmissionCount: _submissionCount,
            LastSubmittedUtc: _lastSubmittedUtc,
            RecentEvents: _recentEvents.ToArray());

    private void EnqueueEventUnsafe(string message)
    {
        _recentEvents.Enqueue(message);
        while (_recentEvents.Count > 8)
        {
            _ = _recentEvents.Dequeue();
        }
    }

    private sealed record SupplierDraftPreset(
        string SupplierName,
        string ContactEmail,
        string RiskTier,
        decimal RequestedBudget);
}

public sealed class DemoDialogActionExecutor(
    IAgentComponentRegistry componentRegistry,
    AgentDialogFormDemoState state) : IDialogActionExecutor
{
    public async Task<ComponentActionExecutionResult> ExecuteAsync(
        DialogActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (handled, componentResult) = await RegisteredComponentExecutorBridge.TryExecuteAsync(
            componentRegistry,
            expectedComponentType: "Dialog",
            componentId: AgentComponentV1CapabilityProfile.AgentDialogComponentId,
            actionId: request.ActionId,
            arguments: request.Arguments,
            cancellationToken);

        var stateResult = state.ApplyDialogAction(request.ActionId);
        return handled ? componentResult : stateResult;
    }
}

public sealed class DemoFormActionExecutor(
    IAgentComponentRegistry componentRegistry,
    AgentDialogFormDemoState state) : IFormActionExecutor
{
    public async Task<ComponentActionExecutionResult> ExecuteAsync(
        FormActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (handled, componentResult) = await RegisteredComponentExecutorBridge.TryExecuteAsync(
            componentRegistry,
            expectedComponentType: "Form",
            componentId: AgentComponentV1CapabilityProfile.AgentFormComponentId,
            actionId: request.ActionId,
            arguments: request.Arguments,
            cancellationToken);

        var stateResult = state.ApplyFormAction(request.ActionId);
        return handled ? componentResult : stateResult;
    }
}

public sealed record AgentDialogFormDemoSnapshot(
    bool DialogVisible,
    SupplierOnboardingDraft Draft,
    bool LastValidationSucceeded,
    IReadOnlyList<string> ValidationErrors,
    int SubmissionCount,
    DateTimeOffset? LastSubmittedUtc,
    IReadOnlyList<string> RecentEvents);

public sealed record SupplierOnboardingDraft
{
    public string SupplierName { get; init; } = string.Empty;

    public string ContactEmail { get; init; } = string.Empty;

    public string RiskTier { get; init; } = string.Empty;

    public decimal RequestedBudget { get; init; }
}
