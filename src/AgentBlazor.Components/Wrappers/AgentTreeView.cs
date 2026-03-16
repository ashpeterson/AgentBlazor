using System.Reflection;
using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Discovery;
using AgentBlazor.Core.Runtime.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;
using MudBlazor.Extensions;
using RuntimeComponentState = AgentBlazor.Core.Runtime.Components.ComponentState;

namespace AgentBlazor.Components;

public class AgentTreeView<T> : MudTreeView<T>, IAgentControllable
{
    private static readonly FieldInfo? RootChildItemsField =
        typeof(MudTreeView<T>).GetField("_childItems", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? ItemChildItemsField =
        typeof(MudTreeViewItem<T>).GetField("_childItems", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? SetSelectedValueMethod =
        typeof(MudTreeView<T>).GetMethod("SetSelectedValueAsync", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? SelectMethod =
        typeof(MudTreeView<T>).GetMethod("SelectAsync", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? OnItemExpandedMethod =
        typeof(MudTreeViewItem<T>).GetMethod("OnItemExpanded", BindingFlags.Instance | BindingFlags.NonPublic);

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
    public Func<T?, string?>? AgentNodeIdSelector { get; set; }

    [Parameter]
    public IEnumerable<string>? NodeIds { get; set; }

    [Parameter]
    public string? SelectedNodeId { get; set; }

    [Parameter]
    public EventCallback<string?> SelectedNodeIdChanged { get; set; }

    [Parameter]
    public IEnumerable<string>? ExpandedNodeIds { get; set; }

    [Parameter]
    public EventCallback<IReadOnlyList<string>> ExpandedNodeIdsChanged { get; set; }

    public string ComponentType => "TreeView";

    private AgentControllableComponentRuntimeSupport? _runtimeSupport;
    private ILogger? _logger;
    private EventCallback<T?> _externalSelectedValueChanged;
    private EventCallback<T?> _wrappedSelectedValueChanged;
    private EventCallback<IReadOnlyCollection<T>?> _externalSelectedValuesChanged;
    private EventCallback<IReadOnlyCollection<T>?> _wrappedSelectedValuesChanged;

    [AgentReadable("Currently selected node id")]
    public string? CurrentSelectedNodeId => GetNodeId(SelectedValue);

    [AgentReadable("Currently selected node ids")]
    public string[] CurrentSelectedNodeIds => GetSelectedNodeIds();

    [AgentReadable("Currently expanded node ids")]
    public string[] CurrentExpandedNodeIds => GetExpandedNodeIds();

    [AgentReadable("Available node ids")]
    public string[] AvailableNodeIds => GetAvailableNodeIds();

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _logger ??= LoggerFactory?.CreateLogger(GetType());
        _runtimeSupport ??= CreateRuntimeSupport();
        _runtimeSupport.OnInitialized();
        EnsureSelectedValueChangedBridge();
        EnsureSelectedValuesChangedBridge();
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
        EnsureCompatibilityState();
        EnsureSelectedValueChangedBridge();
        EnsureSelectedValuesChangedBridge();
        base.OnParametersSet();
        EnsureAgentUserAttributes();
    }

    public ComponentCapability GetCapability() => AgentActionDiscovery.BuildCapability(this);

    public virtual RuntimeComponentState GetCurrentState() => new()
    {
        ["selectedNodeId"] = GetNodeId(SelectedValue),
        ["selectedNodeIds"] = GetSelectedNodeIds(),
        ["expandedNodeIds"] = GetExpandedNodeIds(),
        ["nodeIds"] = GetAvailableNodeIds(),
        ["disabled"] = Disabled,
        ["readOnly"] = ReadOnly,
        ["selectionMode"] = SelectionMode.ToString()
    };

    public virtual async Task<ActionResult> ExecuteActionAsync(
        AgentAction action,
        CancellationToken cancellationToken = default)
    {
        EnsureCompatibilityState();
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

    [AgentAction("Expand one tree node", ActionId = "expand")]
    public async Task<ActionResult> Expand(
        [AgentParam("Node id to expand", Required = true)] string nodeId)
    {
        if (Disabled || ReadOnly)
        {
            return ActionResult.Failure("Cannot expand nodes while tree view is disabled or read-only.");
        }

        if (!TryResolveNode(nodeId, out _, out var renderedItem, out var itemData))
        {
            return ActionResult.NeedsClarification($"Node '{nodeId}' is not available.");
        }

        await SetExpandedSafelyAsync(renderedItem, itemData, true);
        var expandedNodeIds = GetExpandedNodeIds();
        await HandleCompatibilityExpandedNodeIdsChangedAsync(expandedNodeIds);
        await RequestComponentRefreshAsync();
        return ActionResult.Applied($"Expanded node '{nodeId}'.");
    }

    [AgentAction("Collapse one tree node", ActionId = "collapse")]
    public async Task<ActionResult> Collapse(
        [AgentParam("Node id to collapse", Required = true)] string nodeId)
    {
        if (Disabled || ReadOnly)
        {
            return ActionResult.Failure("Cannot collapse nodes while tree view is disabled or read-only.");
        }

        if (!TryResolveNode(nodeId, out _, out var renderedItem, out var itemData))
        {
            return ActionResult.NeedsClarification($"Node '{nodeId}' is not available.");
        }

        await SetExpandedSafelyAsync(renderedItem, itemData, false);
        var expandedNodeIds = GetExpandedNodeIds();
        await HandleCompatibilityExpandedNodeIdsChangedAsync(expandedNodeIds);
        await RequestComponentRefreshAsync();
        return ActionResult.Applied($"Collapsed node '{nodeId}'.");
    }

    [AgentAction("Select one tree node", ActionId = "select_node")]
    public async Task<ActionResult> SelectNode(
        [AgentParam("Node id to select", Required = true)] string nodeId)
    {
        if (Disabled || ReadOnly)
        {
            return ActionResult.Failure("Cannot select nodes while tree view is disabled or read-only.");
        }

        if (!TryResolveNode(nodeId, out var value, out _, out _))
        {
            return ActionResult.NeedsClarification($"Node '{nodeId}' is not available.");
        }

        await SelectNodeSafelyAsync(value);
        await RequestComponentRefreshAsync();
        return ActionResult.Applied($"Selected node '{nodeId}'.");
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

    private void EnsureSelectedValueChangedBridge()
    {
        if (!_wrappedSelectedValueChanged.HasDelegate || !SelectedValueChanged.Equals(_wrappedSelectedValueChanged))
        {
            _externalSelectedValueChanged = SelectedValueChanged;
        }

        _wrappedSelectedValueChanged = EventCallback.Factory.Create<T?>(this, HandleSelectedValueChangedAsync);
        SelectedValueChanged = _wrappedSelectedValueChanged;
    }

    private void EnsureSelectedValuesChangedBridge()
    {
        if (!_wrappedSelectedValuesChanged.HasDelegate || !SelectedValuesChanged.Equals(_wrappedSelectedValuesChanged))
        {
            _externalSelectedValuesChanged = SelectedValuesChanged;
        }

        _wrappedSelectedValuesChanged = EventCallback.Factory.Create<IReadOnlyCollection<T>?>(this, HandleSelectedValuesChangedAsync);
        SelectedValuesChanged = _wrappedSelectedValuesChanged;
    }

    private async Task HandleSelectedValueChangedAsync(T? value)
    {
        SelectedValue = value;
        await HandleCompatibilitySelectedValueChangedAsync(value);

        if (_externalSelectedValueChanged.HasDelegate && !_externalSelectedValueChanged.Equals(_wrappedSelectedValueChanged))
        {
            await _externalSelectedValueChanged.InvokeAsync(value);
        }
    }

    private async Task HandleSelectedValuesChangedAsync(IReadOnlyCollection<T>? values)
    {
        SelectedValues = values;

        if (_externalSelectedValuesChanged.HasDelegate && !_externalSelectedValuesChanged.Equals(_wrappedSelectedValuesChanged))
        {
            await _externalSelectedValuesChanged.InvokeAsync(values);
        }
    }

    private async Task SelectNodeSafelyAsync(T? value)
    {
        if (value is null)
        {
            return;
        }

        if (GetRenderedItems().Count != 0)
        {
            if (SelectionMode == SelectionMode.MultiSelection)
            {
                await InvokeNonPublicTaskAsync(SelectMethod, this, value);
            }
            else
            {
                await InvokeNonPublicTaskAsync(SetSelectedValueMethod, this, value);
            }

            return;
        }

        if (SelectionMode == SelectionMode.MultiSelection)
        {
            var selected = new HashSet<T>(SelectedValues ?? Array.Empty<T>(), Comparer) { value };
            ApplySelectionToItemData(selected);
            await HandleSelectedValuesChangedAsync(selected.ToArray());
            return;
        }

        ApplySelectionToItemData(value);
        await HandleSelectedValueChangedAsync(value);
    }

    private async Task SetExpandedSafelyAsync(
        MudTreeViewItem<T>? renderedItem,
        TreeItemData<T>? itemData,
        bool expanded)
    {
        if (renderedItem is not null)
        {
            await InvokeNonPublicTaskAsync(OnItemExpandedMethod, renderedItem, expanded);
            return;
        }

        if (itemData is not null)
        {
            itemData.Expanded = expanded;
        }
    }

    private void ApplySelectionToItemData(T selectedValue)
    {
        SelectedValue = selectedValue;

        foreach (var item in GetItemDataRecursive(Items))
        {
            item.Selected = Comparer.Equals(item.Value, selectedValue);
        }
    }

    private void ApplySelectionToItemData(HashSet<T> selectedValues)
    {
        SelectedValues = selectedValues.ToArray();

        foreach (var item in GetItemDataRecursive(Items))
        {
            item.Selected = item.Value is not null && selectedValues.Contains(item.Value);
        }
    }

    private bool TryResolveNode(
        string nodeId,
        out T? value,
        out MudTreeViewItem<T>? renderedItem,
        out TreeItemData<T>? itemData)
    {
        foreach (var item in GetRenderedItems())
        {
            if (IsMatch(item.Value, item.Text, nodeId, out value))
            {
                renderedItem = item;
                itemData = null;
                return true;
            }
        }

        foreach (var treeItemData in GetItemDataRecursive(Items))
        {
            if (IsMatch(treeItemData.Value, treeItemData.Text, nodeId, out value))
            {
                renderedItem = null;
                itemData = treeItemData;
                return true;
            }
        }

        value = default;
        renderedItem = null;
        itemData = null;
        return false;
    }

    private bool IsMatch(T? value, string? text, string nodeId, out T? resolvedValue)
    {
        var key = GetNodeId(value, text);
        if (string.Equals(key, nodeId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, nodeId, StringComparison.OrdinalIgnoreCase))
        {
            resolvedValue = ResolveValue(value, text);
            return true;
        }

        resolvedValue = default;
        return false;
    }

    private T? ResolveValue(T? value, string? text)
    {
        if (value is null && typeof(T) == typeof(string) && text is not null)
        {
            return (T)(object)text;
        }

        return value;
    }

    private string[] GetAvailableNodeIds()
    {
        var renderedKeys = GetRenderedItems()
            .Select(GetNodeId)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (renderedKeys.Length != 0)
        {
            return renderedKeys!;
        }

        return GetItemDataRecursive(Items)
            .Select(item => GetNodeId(item.Value, item.Text))
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    private string[] GetSelectedNodeIds()
    {
        if (SelectionMode == SelectionMode.MultiSelection)
        {
            if (SelectedValues is { Count: > 0 })
            {
                return SelectedValues
                    .Select(GetNodeId)
                    .Where(static key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()!;
            }

            return GetRenderedItems()
                .Where(item => item.GetState<bool>(nameof(MudTreeViewItem<T>.Selected)))
                .Select(GetNodeId)
                .Where(static key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()!;
        }

        var selectedNodeId = GetNodeId(SelectedValue);
        return string.IsNullOrWhiteSpace(selectedNodeId) ? [] : [selectedNodeId];
    }

    private string[] GetExpandedNodeIds()
    {
        var renderedExpanded = GetRenderedItems()
            .Where(item => item.GetState<bool>(nameof(MudTreeViewItem<T>.Expanded)))
            .Select(GetNodeId)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (renderedExpanded.Length != 0)
        {
            return renderedExpanded!;
        }

        return GetItemDataRecursive(Items)
            .Where(item => item.Expanded)
            .Select(item => GetNodeId(item.Value, item.Text))
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    private string? GetNodeId(T? value) => GetNodeId(value, value?.ToString());

    private string? GetNodeId(MudTreeViewItem<T> item) => GetNodeId(ResolveValue(item.Value, item.Text), item.Text);

    private string? GetNodeId(T? value, string? fallbackText)
    {
        var selected = AgentNodeIdSelector?.Invoke(value);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            return selected;
        }

        if (value is string stringValue && !string.IsNullOrWhiteSpace(stringValue))
        {
            return stringValue;
        }

        return !string.IsNullOrWhiteSpace(value?.ToString())
            ? value!.ToString()
            : fallbackText;
    }

    private IReadOnlyList<MudTreeViewItem<T>> GetRenderedItems()
    {
        var roots = RootChildItemsField?.GetValue(this) as System.Collections.IEnumerable;
        if (roots is null)
        {
            return [];
        }

        var items = new List<MudTreeViewItem<T>>();
        foreach (var root in roots.OfType<MudTreeViewItem<T>>())
        {
            AddRenderedItemTree(root, items);
        }

        return items;
    }

    private void AddRenderedItemTree(MudTreeViewItem<T> item, List<MudTreeViewItem<T>> items)
    {
        items.Add(item);

        var children = ItemChildItemsField?.GetValue(item) as System.Collections.IEnumerable;
        if (children is null)
        {
            return;
        }

        foreach (var child in children.OfType<MudTreeViewItem<T>>())
        {
            AddRenderedItemTree(child, items);
        }
    }

    private static IEnumerable<TreeItemData<T>> GetItemDataRecursive(IEnumerable<TreeItemData<T>>? items)
    {
        if (items is null)
        {
            yield break;
        }

        foreach (var item in items)
        {
            yield return item;

            if (item.Children is null)
            {
                continue;
            }

            foreach (var child in GetItemDataRecursive(item.Children.OfType<TreeItemData<T>>()))
            {
                yield return child;
            }
        }
    }

    private static async Task InvokeNonPublicTaskAsync(MethodInfo? method, object target, params object?[] args)
    {
        if (method is null)
        {
            throw new InvalidOperationException("Expected MudBlazor tree view method was not found.");
        }

        if (method.Invoke(target, args) is Task task)
        {
            await task;
        }
    }

    private void EnsureAgentUserAttributes()
    {
        UserAttributes ??= [];
        if (!UserAttributes.ContainsKey("id"))
        {
            UserAttributes["id"] = AgentId;
        }

        UserAttributes["data-ab-type"] = "tree-view";
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
    
    private bool IsStringCompatibilityMode()
    {
        return typeof(T) == typeof(string);
    }

    private bool ShouldUseCompatibilityItems()
    {
        return IsStringCompatibilityMode()
            && ChildContent is null
            && (Items is null || Items.Count == 0)
            && NodeIds is not null;
    }

    private bool ShouldApplySelectedNodeAlias()
    {
        return IsStringCompatibilityMode()
            && (NodeIds is not null
                || SelectedNodeIdChanged.HasDelegate
                || SelectedNodeId is not null);
    }

    private void EnsureCompatibilityState()
    {
        if (ShouldUseCompatibilityItems())
        {
            Items = BuildCompatibilityItems();
        }

        if (ShouldApplySelectedNodeAlias())
        {
            var selectedValue = SelectedValue as string;
            if (!string.Equals(selectedValue, SelectedNodeId, StringComparison.OrdinalIgnoreCase))
            {
                SelectedValue = SelectedNodeId is null ? default : (T)(object)SelectedNodeId;
            }
        }
    }

    private async Task HandleCompatibilitySelectedValueChangedAsync(T? value)
    {
        if (!IsStringCompatibilityMode())
        {
            return;
        }

        SelectedNodeId = value as string;
        if (SelectedNodeIdChanged.HasDelegate)
        {
            await SelectedNodeIdChanged.InvokeAsync(SelectedNodeId);
        }
    }

    private async Task HandleCompatibilityExpandedNodeIdsChangedAsync(IReadOnlyList<string> nodeIds)
    {
        if (!IsStringCompatibilityMode())
        {
            return;
        }

        ExpandedNodeIds = nodeIds;
        if (ExpandedNodeIdsChanged.HasDelegate)
        {
            await ExpandedNodeIdsChanged.InvokeAsync(nodeIds);
        }
    }

    private IReadOnlyCollection<TreeItemData<T>> BuildCompatibilityItems()
    {
        var expanded = new HashSet<string>(
            ExpandedNodeIds?.Where(static nodeId => !string.IsNullOrWhiteSpace(nodeId)) ?? [],
            StringComparer.OrdinalIgnoreCase);

        return NodeIds?
            .Where(static nodeId => !string.IsNullOrWhiteSpace(nodeId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(nodeId => new TreeItemData<T>
            {
                Value = (T)(object)nodeId,
                Text = nodeId,
                Expanded = expanded.Contains(nodeId),
                Selected = string.Equals(nodeId, SelectedNodeId, StringComparison.OrdinalIgnoreCase)
            })
            .ToArray()
            ?? [];
    }
}
