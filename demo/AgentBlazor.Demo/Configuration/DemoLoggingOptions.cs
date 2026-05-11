namespace AgentBlazor.Demo.Configuration;

internal sealed class DemoLoggingOptions
{
    public const string SectionName = "DemoLogging";

    public bool Enabled { get; set; } = true;

    public string DirectoryPath { get; set; } = Path.Combine(Path.GetTempPath(), "agentblazor-demo-logs");

    public string FileName { get; set; } = "chat-requests.jsonl";

    public string TrafficFileName { get; set; } = "traffic-requests.jsonl";

    public string? AccessToken { get; set; }

    public bool IncludePromptPreview { get; set; }

    public int PromptPreviewMaxLength { get; set; } = 160;

    public int MaxTailLines { get; set; } = 500;
}
