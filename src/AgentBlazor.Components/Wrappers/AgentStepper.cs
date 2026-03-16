using System.Reflection;
using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Discovery;
using AgentBlazor.Core.Runtime.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using MudBlazor;
using RuntimeComponentState = AgentBlazor.Core.Runtime.Components.ComponentState;

namespace AgentBlazor.Components;

public class AgentStepper : MudStepper, IAgentControllable, IDisposable
{
    private static readonly MethodInfo? SetActiveIndexAsyncMethod =
        typeof(MudStepper).GetMethod("SetActiveIndexAsync", BindingFlags.Instance | BindingFlags.NonPublic);

    [Inject]
    private IAgentComponentRegistry ComponentRegistry { get; set; } = default!;

    [Inject]
    private IAgentNavigationIntentService NavigationIntentService { get; set; } = default!;

    [Inject]
    private NavigationManager? Navigation { get; set; }

    [Inject]
    private ILoggerFactory? LoggerFactory { get; set; }

    [Inject]
    private IAgentDeferredActionEvents? DeferredActionEvents { get; set; }

    [Parameter]
    public string AgentId { get; set; } = string.Empty;

    [Parameter]
    public int CurrentStepIndex { get; set; }

    [Parameter]
    public EventCallback<int> CurrentStepIndexChanged { get; set; }

    [Parameter]
    public IEnumerable<string>? StepIds { get; set; }

    [Parameter]
    public int? TotalSteps { get; set; }

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public bool ReadOnly { get; set; }

    public string ComponentType => "Stepper";

    private AgentControllableComponentRuntimeSupport? _runtimeSupport;
    private ILogger? _logger;
    private EventCallback<int> _externalActiveIndexChanged;
    private EventCallback<int> _wrappedActiveIndexChanged;
    private bool _hasRendered;
    private bool _currentStepIndexParameterSupplied;
    private bool _activeIndexParameterSupplied;

    [AgentReadable("Current step index (0-based)")]
    public int CurrentIndex => GetEffectiveCurrentStepIndex();

    [AgentReadable("Known step identifiers")]
    public string[] KnownStepIds => GetKnownStepIds().ToArray();

    [AgentReadable("Total known step count")]
    public int KnownTotalSteps => GetKnownTotalSteps();

    public override async Task SetParametersAsync(ParameterView parameters)
    {
        _currentStepIndexParameterSupplied = parameters.TryGetValue<int>(nameof(CurrentStepIndex), out _);
        _activeIndexParameterSupplied = parameters.TryGetValue<int>(nameof(ActiveIndex), out _);
        await base.SetParametersAsync(parameters);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _logger ??= LoggerFactory?.CreateLogger(GetType());
        _runtimeSupport ??= CreateRuntimeSupport();
        _runtimeSupport.OnInitialized();
        EnsureActiveIndexChangedBridge();
        EnsureAgentUserAttributes();
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        _runtimeSupport ??= CreateRuntimeSupport();
        await _runtimeSupport.OnInitializedAsync();
    }

    protected override void OnParametersSet()
    {
        EnsureActiveIndexChangedBridge();

        if (ShouldApplyCurrentStepAlias())
        {
            ActiveIndex = CurrentStepIndex;
        }
        else
        {
            CurrentStepIndex = ActiveIndex;
        }

        base.OnParametersSet();
        EnsureAgentUserAttributes();
    }

    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);
        if (firstRender)
        {
            _hasRendered = true;
        }
    }

    public void Dispose()
    {
        _runtimeSupport?.Dispose();
    }

    public ComponentCapability GetCapability() => AgentActionDiscovery.BuildCapability(this);

    public virtual RuntimeComponentState GetCurrentState() => new()
    {
        ["currentStepIndex"] = GetEffectiveCurrentStepIndex(),
        ["totalSteps"] = GetKnownTotalSteps(),
        ["stepIds"] = GetKnownStepIds().ToArray(),
        ["canGoNext"] = CanMoveNext(),
        ["canGoPrevious"] = CanMovePrevious(),
        ["disabled"] = Disabled,
        ["readOnly"] = ReadOnly
    };

    public virtual async Task<ActionResult> ExecuteActionAsync(
        AgentAction action,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ActionResult result = ActionResult.Applied($"Executed '{action.Name}'.");
            await InvokeAsync(async () =>
            {
                result = await AgentActionDiscovery.ExecuteActionAsync(this, action, cancellationToken);
            });
            return result;
        }
        catch (InvalidOperationException)
        {
            return await AgentActionDiscovery.ExecuteActionAsync(this, action, cancellationToken);
        }
    }

    [AgentAction("Go to an exact step index (0-based)", ActionId = "go_to_step")]
    public async Task<ActionResult> GoToStep(
        [AgentParam("Step index to activate", Required = true)] int index)
    {
        if (Disabled || ReadOnly)
        {
            return ActionResult.Failure("Cannot navigate steps while stepper is disabled or read-only.");
        }

        if (index < 0)
        {
            return ActionResult.NeedsClarification("Step index must be 0 or greater.");
        }

        var total = GetKnownTotalSteps();
        if (total > 0 && index >= total)
        {
            return ActionResult.NeedsClarification($"Step index {index} is out of range. Max index is {total - 1}.");
        }

        if (!await PreviewStepChangeAsync(index, StepAction.Activate))
        {
            return ActionResult.Failure($"Navigation to step {index} was cancelled.");
        }

        await SetActiveIndexSafelyAsync(index);
        await NotifyTargetStepClickedAsync(index);
        await RequestComponentRefreshAsync();
        return ActionResult.Applied($"Moved to step {GetEffectiveCurrentStepIndex()}.");
    }

    [AgentAction("Move to the next step", ActionId = "next")]
    public async Task<ActionResult> Next()
    {
        if (Disabled || ReadOnly)
        {
            return ActionResult.Failure("Cannot move next while stepper is disabled or read-only.");
        }

        if (!CanMoveNext())
        {
            return ActionResult.Failure("Already at the last available step.");
        }

        if (CanUseNativeStepperNavigation())
        {
            await NextStepAsync();
            await SyncCurrentStepIndexAsync(ActiveIndex);
        }
        else
        {
            var nextIndex = Math.Max(0, GetEffectiveCurrentStepIndex() + 1);
            await SetHeadlessIndexAsync(nextIndex);
        }

        await RequestComponentRefreshAsync();
        return ActionResult.Applied($"Moved to step {GetEffectiveCurrentStepIndex()}.");
    }

    [AgentAction("Move to the previous step", ActionId = "previous")]
    public async Task<ActionResult> Previous()
    {
        if (Disabled || ReadOnly)
        {
            return ActionResult.Failure("Cannot move previous while stepper is disabled or read-only.");
        }

        if (!CanMovePrevious())
        {
            return ActionResult.Failure("Already at the first step.");
        }

        if (CanUseNativeStepperNavigation())
        {
            await PreviousStepAsync();
            await SyncCurrentStepIndexAsync(ActiveIndex);
        }
        else
        {
            await SetHeadlessIndexAsync(GetEffectiveCurrentStepIndex() - 1);
        }

        await RequestComponentRefreshAsync();
        return ActionResult.Applied($"Moved to step {GetEffectiveCurrentStepIndex()}.");
    }

    private AgentControllableComponentRuntimeSupport CreateRuntimeSupport()
    {
        return new AgentControllableComponentRuntimeSupport(
            componentType: GetType(),
            component: this,
            componentRegistry: ComponentRegistry,
            navigationIntentService: NavigationIntentService,
            navigation: Navigation,
            logger: _logger,
            deferredActionEvents: DeferredActionEvents,
            getComponentType: () => ComponentType,
            getAgentId: () => AgentId,
            setAgentId: value => AgentId = value,
            executeActionAsync: action => ExecuteActionAsync(action),
            requestComponentRefreshAsync: RequestComponentRefreshAsync);
    }

    private void EnsureActiveIndexChangedBridge()
    {
        if (!_wrappedActiveIndexChanged.HasDelegate || !ActiveIndexChanged.Equals(_wrappedActiveIndexChanged))
        {
            _externalActiveIndexChanged = ActiveIndexChanged;
        }

        _wrappedActiveIndexChanged = EventCallback.Factory.Create<int>(this, HandleActiveIndexChangedAsync);
        ActiveIndexChanged = _wrappedActiveIndexChanged;
    }

    private async Task HandleActiveIndexChangedAsync(int index)
    {
        CurrentStepIndex = index;

        if (CurrentStepIndexChanged.HasDelegate)
        {
            await CurrentStepIndexChanged.InvokeAsync(index);
        }

        if (_externalActiveIndexChanged.HasDelegate && !_externalActiveIndexChanged.Equals(_wrappedActiveIndexChanged))
        {
            await _externalActiveIndexChanged.InvokeAsync(index);
        }
    }

    private bool ShouldApplyCurrentStepAlias()
    {
        return !_activeIndexParameterSupplied && (_currentStepIndexParameterSupplied || CurrentStepIndexChanged.HasDelegate);
    }

    private int GetEffectiveCurrentStepIndex()
    {
        if (Steps.Count > 0 || _hasRendered)
        {
            return ActiveIndex;
        }

        return CurrentStepIndex;
    }

    private IReadOnlyList<string> GetKnownStepIds()
    {
        var nativeStepIds = Steps
            .Select(static step => step.Title)
            .Where(static title => !string.IsNullOrWhiteSpace(title))
            .Select(static title => title!)
            .ToArray();

        if (nativeStepIds.Length > 0)
        {
            return nativeStepIds;
        }

        return StepIds?
            .Where(static stepId => !string.IsNullOrWhiteSpace(stepId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
    }

    private int GetKnownTotalSteps()
    {
        if (Steps.Count > 0)
        {
            return Steps.Count;
        }

        var stepIds = GetKnownStepIds();
        if (stepIds.Count > 0)
        {
            return stepIds.Count;
        }

        return TotalSteps.GetValueOrDefault(0);
    }

    private bool CanMoveNext()
    {
        if (Disabled || ReadOnly)
        {
            return false;
        }

        if (CanUseNativeStepperNavigation())
        {
            return CanGoToNextStep;
        }

        var total = GetKnownTotalSteps();
        return total <= 0 || GetEffectiveCurrentStepIndex() < total - 1;
    }

    private bool CanMovePrevious()
    {
        if (Disabled || ReadOnly)
        {
            return false;
        }

        if (CanUseNativeStepperNavigation())
        {
            return PreviousStepEnabled;
        }

        return GetEffectiveCurrentStepIndex() > 0;
    }

    private bool CanUseNativeStepperNavigation() => _hasRendered && Steps.Count > 0;

    private async Task<bool> PreviewStepChangeAsync(int index, StepAction action)
    {
        if (OnPreviewInteraction is null)
        {
            return true;
        }

        var args = new StepperInteractionEventArgs
        {
            StepIndex = index,
            Action = action
        };

        await OnPreviewInteraction.Invoke(args);
        return !args.Cancel;
    }

    private async Task SetActiveIndexSafelyAsync(int index)
    {
        if (!CanUseNativeStepperNavigation() || SetActiveIndexAsyncMethod is null)
        {
            await SetHeadlessIndexAsync(index);
            return;
        }

        try
        {
            var task = (Task?)SetActiveIndexAsyncMethod.Invoke(this, [index, false]);
            if (task is not null)
            {
                await task;
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            await SetHeadlessIndexAsync(index);
            return;
        }
        catch (InvalidOperationException)
        {
            await SetHeadlessIndexAsync(index);
            return;
        }

        await SyncCurrentStepIndexAsync(ActiveIndex);
    }

    private async Task SetHeadlessIndexAsync(int index)
    {
        ActiveIndex = index;
        await SyncCurrentStepIndexAsync(index);
    }

    private async Task SyncCurrentStepIndexAsync(int index)
    {
        CurrentStepIndex = index;
        await HandleActiveIndexChangedAsync(index);
    }

    private async Task NotifyTargetStepClickedAsync(int index)
    {
        if (index < 0 || index >= Steps.Count)
        {
            return;
        }

        if (Steps[index] is MudStep step && step.OnClick.HasDelegate)
        {
            await step.OnClick.InvokeAsync(new MouseEventArgs());
        }
    }

    private void EnsureAgentUserAttributes()
    {
        UserAttributes ??= [];
        if (!UserAttributes.ContainsKey("id"))
        {
            UserAttributes["id"] = AgentId;
        }

        UserAttributes["data-ab-type"] = "stepper";
        UserAttributes["data-ab-agentid"] = AgentId;
    }

    private Task RequestComponentRefreshAsync()
    {
        try
        {
            return InvokeAsync(StateHasChanged);
        }
        catch (InvalidOperationException)
        {
            return Task.CompletedTask;
        }
    }
}
