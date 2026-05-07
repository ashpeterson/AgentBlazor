using System.Text.Json;
using AgentBlazor.Demo.Configuration;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Demo.Services;

internal sealed class JsonlDemoChatRequestLog : IDemoChatRequestLog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DemoLoggingOptions _options;

    public JsonlDemoChatRequestLog(IOptions<DemoLoggingOptions> options)
    {
        _options = options.Value;
        LogFilePath = Path.Combine(_options.DirectoryPath, _options.FileName);
    }

    public string LogFilePath { get; }

    public async Task AppendAsync(DemoChatRequestLogEntry entry, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var line = JsonSerializer.Serialize(entry, JsonOptions);
        Directory.CreateDirectory(_options.DirectoryPath);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(LogFilePath, line + Environment.NewLine, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> ReadTailAsync(int lineCount, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !File.Exists(LogFilePath))
        {
            return [];
        }

        var boundedLineCount = Math.Clamp(lineCount, 1, Math.Max(1, _options.MaxTailLines));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var lines = await File.ReadAllLinesAsync(LogFilePath, cancellationToken);
            return lines.Length <= boundedLineCount
                ? lines
                : lines[^boundedLineCount..];
        }
        finally
        {
            _gate.Release();
        }
    }
}
