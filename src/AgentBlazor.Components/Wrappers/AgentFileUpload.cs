using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Discovery;
using AgentBlazor.Core.Runtime.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace AgentBlazor.Components;

public class AgentFileUpload<T> : MudFileUpload<T>, IAgentControllable, IDisposable
{
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
    public IReadOnlyList<string>? FileNames { get; set; }

    [Parameter]
    public EventCallback<IReadOnlyList<string>> FileNamesChanged { get; set; }

    public string ComponentType => "FileUpload";

    private AgentControllableComponentRuntimeSupport? _runtimeSupport;
    private ILogger? _logger;
    private EventCallback<T?> _externalFilesChanged;
    private EventCallback<T?> _wrappedFilesChanged;

    [AgentReadable("Attached file names")]
    public string[] AttachedFiles => GetEffectiveFileNames().ToArray();

    [AgentReadable("Attached file count")]
    public int AttachedFileCount => GetEffectiveFileNames().Count;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _logger ??= LoggerFactory?.CreateLogger(GetType());
        _runtimeSupport ??= CreateRuntimeSupport();
        _runtimeSupport.OnInitialized();
        EnsureFilesChangedBridge();
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
        EnsureFilesChangedBridge();
        EnsureCompatibilityState();
        base.OnParametersSet();
        EnsureAgentUserAttributes();
    }

    public void Dispose()
    {
        _runtimeSupport?.Dispose();
    }

    public ComponentCapability GetCapability() => AgentActionDiscovery.BuildCapability(this);

    public virtual ComponentState GetCurrentState() => new()
    {
        ["files"] = GetEffectiveFileNames().ToArray(),
        ["fileCount"] = GetEffectiveFileNames().Count,
        ["disabled"] = Disabled,
        ["readOnly"] = false
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

    [AgentAction("Attach one file name to the current upload list", ActionId = "attach")]
    public async Task<ActionResult> Attach(
        [AgentParam("File name to attach", Required = true)] string fileName)
    {
        if (Disabled)
        {
            return ActionResult.Failure("Cannot attach files while upload is disabled.");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return ActionResult.NeedsClarification("File name is required.");
        }

        if (!SupportsBrowserFileValue())
        {
            return ActionResult.Failure("Attach by file name is only supported when T is IBrowserFile or IReadOnlyList<IBrowserFile>.");
        }

        if (HasConcreteBrowserFiles())
        {
            return ActionResult.Failure(
                "Attach by file name cannot synthesize a real browser file while concrete browser files are already selected. Use the host upload UI or FileNames-only mode.");
        }

        var updatedFiles = GetEffectiveFileNames()
            .Append(fileName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await ApplyFileNamesAsync(updatedFiles);
        await RequestComponentRefreshAsync();
        return ActionResult.Applied($"Attached '{fileName}'.");
    }

    [AgentAction("Remove one file name from the current upload list", ActionId = "remove")]
    public async Task<ActionResult> Remove(
        [AgentParam("File name to remove", Required = true)] string fileName)
    {
        if (Disabled)
        {
            return ActionResult.Failure("Cannot remove files while upload is disabled.");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return ActionResult.NeedsClarification("File name is required.");
        }

        if (SupportsBrowserFileValue())
        {
            var removed = await RemoveFileByNameAsync(fileName.Trim());
            if (!removed)
            {
                return ActionResult.NeedsClarification($"File '{fileName}' is not attached.");
            }

            await RequestComponentRefreshAsync();
            return ActionResult.Applied($"Removed '{fileName}'.");
        }

        var updated = GetEffectiveFileNames()
            .Where(existing => !string.Equals(existing, fileName.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (updated.Length == GetEffectiveFileNames().Count)
        {
            return ActionResult.NeedsClarification($"File '{fileName}' is not attached.");
        }

        await PublishFileNamesOnlyAsync(updated);
        await RequestComponentRefreshAsync();
        return ActionResult.Applied($"Removed '{fileName}'.");
    }

    [AgentAction("List currently attached files", ActionId = "list_files")]
    public Task<ActionResult> ListFiles()
    {
        var files = GetEffectiveFileNames();
        if (files.Count == 0)
        {
            return Task.FromResult(ActionResult.Applied("No files are attached."));
        }

        return Task.FromResult(ActionResult.Applied($"Attached files: {string.Join(", ", files)}"));
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

    private void EnsureFilesChangedBridge()
    {
        if (!_wrappedFilesChanged.HasDelegate || !FilesChanged.Equals(_wrappedFilesChanged))
        {
            _externalFilesChanged = FilesChanged;
        }

        _wrappedFilesChanged = EventCallback.Factory.Create<T?>(this, HandleFilesChangedAsync);
        FilesChanged = _wrappedFilesChanged;
    }

    private async Task HandleFilesChangedAsync(T? files)
    {
        Files = files;

        if (ShouldUseFileNameCompatibilityMode())
        {
            FileNames = ExtractFileNames(files);
            if (FileNamesChanged.HasDelegate)
            {
                await FileNamesChanged.InvokeAsync(FileNames);
            }
        }

        if (_externalFilesChanged.HasDelegate && !_externalFilesChanged.Equals(_wrappedFilesChanged))
        {
            await _externalFilesChanged.InvokeAsync(files);
        }
    }

    private void EnsureCompatibilityState()
    {
        if (!ShouldUseFileNameCompatibilityMode() || !SupportsBrowserFileValue())
        {
            return;
        }

        if (!CanSafelyReplaceCurrentFiles())
        {
            return;
        }

        var normalizedNames = NormalizeFileNames(FileNames);
        var currentNames = NormalizeFileNames(ExtractFileNames(Files));
        if (normalizedNames.SequenceEqual(currentNames, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        Files = BuildBrowserFileValue(normalizedNames);
    }

    private IReadOnlyList<string> GetEffectiveFileNames()
    {
        if (SupportsBrowserFileValue() && HasConcreteBrowserFiles())
        {
            return NormalizeFileNames(ExtractFileNames(Files));
        }

        if (ShouldUseFileNameCompatibilityMode())
        {
            return NormalizeFileNames(FileNames);
        }

        if (SupportsBrowserFileValue())
        {
            return NormalizeFileNames(ExtractFileNames(Files));
        }

        return NormalizeFileNames(FileNames);
    }

    private bool ShouldUseFileNameCompatibilityMode()
    {
        return FileNamesChanged.HasDelegate || FileNames is not null;
    }

    private bool SupportsBrowserFileValue()
    {
        return typeof(T) == typeof(IBrowserFile) || typeof(T) == typeof(IReadOnlyList<IBrowserFile>);
    }

    private bool CanSafelyReplaceCurrentFiles()
    {
        return Files switch
        {
            null => true,
            NamedBrowserFile => true,
            IReadOnlyList<IBrowserFile> fileList => fileList.All(static file => file is NamedBrowserFile),
            _ => false
        };
    }

    private bool HasConcreteBrowserFiles()
    {
        return Files switch
        {
            IBrowserFile file => file is not NamedBrowserFile,
            IReadOnlyList<IBrowserFile> fileList => fileList.Any(static file => file is not NamedBrowserFile),
            _ => false
        };
    }

    private async Task ApplyFileNamesAsync(IReadOnlyList<string> fileNames)
    {
        if (!SupportsBrowserFileValue())
        {
            await PublishFileNamesOnlyAsync(fileNames);
            return;
        }

        if (!CanSafelyReplaceCurrentFiles())
        {
            await PublishFileNamesOnlyAsync(fileNames);
            return;
        }

        await HandleFilesChangedAsync(BuildBrowserFileValue(fileNames));
    }

    private async Task<bool> RemoveFileByNameAsync(string fileName)
    {
        switch (Files)
        {
            case IBrowserFile singleFile when string.Equals(singleFile.Name, fileName, StringComparison.OrdinalIgnoreCase):
                await HandleFilesChangedAsync(default);
                return true;

            case IReadOnlyList<IBrowserFile> fileList:
            {
                var updatedFiles = fileList
                    .Where(file => !string.Equals(file.Name, fileName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (updatedFiles.Length == fileList.Count)
                {
                    return false;
                }

                if (updatedFiles.Length == 0)
                {
                    await HandleFilesChangedAsync(default);
                }
                else
                {
                    await HandleFilesChangedAsync((T)(object)(IReadOnlyList<IBrowserFile>)updatedFiles);
                }

                return true;
            }
        }

        var updatedFileNames = GetEffectiveFileNames()
            .Where(existing => !string.Equals(existing, fileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (updatedFileNames.Length == GetEffectiveFileNames().Count)
        {
            return false;
        }

        await ApplyFileNamesAsync(updatedFileNames);
        return true;
    }

    private async Task PublishFileNamesOnlyAsync(IReadOnlyList<string> fileNames)
    {
        FileNames = NormalizeFileNames(fileNames);
        if (FileNamesChanged.HasDelegate)
        {
            await FileNamesChanged.InvokeAsync(FileNames);
        }
    }

    private T? BuildBrowserFileValue(IReadOnlyList<string> fileNames)
    {
        var normalizedNames = NormalizeFileNames(fileNames);

        if (typeof(T) == typeof(IBrowserFile))
        {
            return normalizedNames.Count == 0 ? default : (T)(object)new NamedBrowserFile(normalizedNames[0]);
        }

        if (typeof(T) == typeof(IReadOnlyList<IBrowserFile>))
        {
            var files = normalizedNames.Select(static name => (IBrowserFile)new NamedBrowserFile(name)).ToArray();
            return (T)(object)files;
        }

        return default;
    }

    private static IReadOnlyList<string> ExtractFileNames(T? files)
    {
        return files switch
        {
            IBrowserFile singleFile => [singleFile.Name],
            IReadOnlyList<IBrowserFile> fileList => NormalizeFileNames(fileList.Select(static file => file.Name)),
            _ => []
        };
    }

    private static IReadOnlyList<string> NormalizeFileNames(IEnumerable<string>? fileNames)
    {
        if (fileNames is null)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<string>();

        foreach (var fileName in fileNames)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            var trimmed = fileName.Trim();
            if (seen.Add(trimmed))
            {
                normalized.Add(trimmed);
            }
        }

        return normalized;
    }

    private void EnsureAgentUserAttributes()
    {
        UserAttributes ??= [];
        if (!UserAttributes.ContainsKey("id"))
        {
            UserAttributes["id"] = AgentId;
        }

        UserAttributes["data-ab-type"] = "file-upload";
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

    private sealed class NamedBrowserFile(string name) : IBrowserFile
    {
        public string Name { get; } = name;

        public DateTimeOffset LastModified { get; } = DateTimeOffset.UtcNow;

        public long Size { get; } = 0;

        public string ContentType { get; } = "application/octet-stream";

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            _ = maxAllowedSize;
            _ = cancellationToken;
            return Stream.Null;
        }
    }
}
