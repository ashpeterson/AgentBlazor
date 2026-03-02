namespace AgentBlazor.Core.Runtime.Tools;

public sealed record AgentServiceTool(
    string Name,
    string Description,
    IReadOnlyList<AgentToolParameter> Parameters,
    Func<IReadOnlyDictionary<string, object?>, IServiceProvider, CancellationToken, Task<string>> Handler);
