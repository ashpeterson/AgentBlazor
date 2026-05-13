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

    public decimal InputTokenCostPerMillion { get; set; } = 0.15m;

    public decimal OutputTokenCostPerMillion { get; set; } = 0.60m;

    public bool DailyCostLimitEnabled { get; set; } = true;

    public decimal DailyCostLimitUsd { get; set; } = 2.00m;
}
