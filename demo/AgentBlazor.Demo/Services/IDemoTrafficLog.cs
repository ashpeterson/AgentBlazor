namespace AgentBlazor.Demo.Services;

internal interface IDemoTrafficLog
{
    string LogFilePath { get; }

    Task AppendAsync(DemoTrafficLogEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ReadTailAsync(int lineCount, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ReadAllAsync(CancellationToken cancellationToken = default);
}
