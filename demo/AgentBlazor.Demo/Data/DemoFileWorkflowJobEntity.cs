namespace AgentBlazor.Demo.Data;

internal sealed class DemoFileWorkflowJobEntity
{
    public int Id { get; set; }

    public string SessionKey { get; set; } = string.Empty;

    public string JobId { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string UploadMode { get; set; } = "Local";

    public string Status { get; set; } = "Pending";

    public string? StorageToken { get; set; }

    public string Message { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }
}
