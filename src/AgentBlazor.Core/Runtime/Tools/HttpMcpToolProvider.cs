using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentBlazor.Core.Runtime.Tools;

/// <summary>
/// Connects to an MCP (Model Context Protocol) server over HTTP and exposes its tools
/// as <see cref="AgentServiceTool"/> records. Uses the MCP JSON-RPC protocol directly.
/// </summary>
public sealed class HttpMcpToolProvider : IMcpToolProvider
{
    private readonly string _serverUrl;
    private readonly IReadOnlyList<string>? _includeTools;
    private readonly IReadOnlyList<string>? _excludeTools;
    private readonly Lazy<HttpClient> _httpClient;
    private IReadOnlyList<AgentServiceTool>? _cachedTools;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public HttpMcpToolProvider(
        string serverUrl,
        IReadOnlyList<string>? includeTools = null,
        IReadOnlyList<string>? excludeTools = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverUrl);
        _serverUrl = serverUrl.TrimEnd('/');
        _includeTools = includeTools;
        _excludeTools = excludeTools;
        _httpClient = new Lazy<HttpClient>(() => new HttpClient { BaseAddress = new Uri(_serverUrl + "/") });
    }

    public async Task<IReadOnlyList<AgentServiceTool>> GetToolsAsync(CancellationToken ct = default)
    {
        if (_cachedTools is not null)
            return _cachedTools;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cachedTools is not null)
                return _cachedTools;

            _cachedTools = await FetchToolsFromServerAsync(ct);
            return _cachedTools;
        }
        catch
        {
            // Return empty on connection failure; will retry next turn
            return [];
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IReadOnlyList<AgentServiceTool>> FetchToolsFromServerAsync(CancellationToken ct)
    {
        var rpc = new McpJsonRpcRequest
        {
            JsonRpc = "2.0",
            Id = 1,
            Method = "tools/list",
            Params = new { }
        };

        using var response = await _httpClient.Value.PostAsJsonAsync(
            string.Empty, rpc, JsonOptions, ct);

        response.EnsureSuccessStatusCode();

        var rpcResponse = await response.Content.ReadFromJsonAsync<McpJsonRpcResponse>(JsonOptions, ct);
        if (rpcResponse?.Result?.Tools is null)
            return [];

        var tools = new List<AgentServiceTool>();
        foreach (var mcpTool in rpcResponse.Result.Tools)
        {
            if (string.IsNullOrWhiteSpace(mcpTool.Name)) continue;

            if (!PassesFilter(mcpTool.Name)) continue;

            var parameters = BuildParameters(mcpTool);
            var capturedName = mcpTool.Name;

            tools.Add(new AgentServiceTool(
                Name: capturedName,
                Description: mcpTool.Description ?? capturedName,
                Parameters: parameters,
                Handler: async (args, _, innerCt) => await CallToolAsync(capturedName, args, innerCt)));
        }

        return tools;
    }

    private bool PassesFilter(string toolName)
    {
        if (_includeTools is { Count: > 0 } &&
            !_includeTools.Any(t => string.Equals(t, toolName, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (_excludeTools is { Count: > 0 } &&
            _excludeTools.Any(t => string.Equals(t, toolName, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    private static IReadOnlyList<AgentToolParameter> BuildParameters(McpToolDefinition tool)
    {
        if (tool.InputSchema?.Properties is not { } props)
            return [];

        var required = tool.InputSchema.Required ?? [];
        var parameters = new List<AgentToolParameter>(props.Count);
        foreach (var (name, schema) in props)
        {
            parameters.Add(new AgentToolParameter(
                Name: name,
                Description: schema.Description ?? name,
                Type: schema.Type ?? "string",
                Required: required.Contains(name, StringComparer.OrdinalIgnoreCase)));
        }

        return parameters;
    }

    private async Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken ct)
    {
        var rpc = new McpJsonRpcRequest
        {
            JsonRpc = "2.0",
            Id = 2,
            Method = "tools/call",
            Params = new { name = toolName, arguments = args }
        };

        using var response = await _httpClient.Value.PostAsJsonAsync(
            string.Empty, rpc, JsonOptions, ct);

        response.EnsureSuccessStatusCode();

        var rpcResponse = await response.Content.ReadFromJsonAsync<McpToolCallResponse>(JsonOptions, ct);
        return rpcResponse?.Result?.Content?.FirstOrDefault()?.Text ?? string.Empty;
    }

    // ── Private DTOs ──────────────────────────────────────────────────────────

    private sealed class McpJsonRpcRequest
    {
        public string JsonRpc { get; init; } = "2.0";
        public int Id { get; init; }
        public required string Method { get; init; }
        public object? Params { get; init; }
    }

    private sealed class McpJsonRpcResponse
    {
        public McpToolListResult? Result { get; init; }
    }

    private sealed class McpToolListResult
    {
        public List<McpToolDefinition>? Tools { get; init; }
    }

    private sealed class McpToolDefinition
    {
        public string? Name { get; init; }
        public string? Description { get; init; }
        public McpInputSchema? InputSchema { get; init; }
    }

    private sealed class McpInputSchema
    {
        public Dictionary<string, McpPropertySchema>? Properties { get; init; }
        public List<string>? Required { get; init; }
    }

    private sealed class McpPropertySchema
    {
        public string? Type { get; init; }
        public string? Description { get; init; }
    }

    private sealed class McpToolCallResponse
    {
        public McpToolCallResult? Result { get; init; }
    }

    private sealed class McpToolCallResult
    {
        public List<McpContent>? Content { get; init; }
    }

    private sealed class McpContent
    {
        public string? Type { get; init; }
        public string? Text { get; init; }
    }
}
