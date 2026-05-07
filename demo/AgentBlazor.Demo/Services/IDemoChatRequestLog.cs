namespace AgentBlazor.Demo.Services;

internal interface IDemoChatRequestLog
{
    string LogFilePath { get; }

    Task AppendAsync(DemoChatRequestLogEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ReadTailAsync(int lineCount, CancellationToken cancellationToken = default);
}
