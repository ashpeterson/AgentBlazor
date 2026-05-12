using System.Text.Json.Serialization;

namespace AgentBlazor.Demo.Services;

internal sealed record DemoChatRequestLogEntry
{
    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("route")]
    public string? Route { get; init; }

    [JsonPropertyName("agentName")]
    public string? AgentName { get; init; }

    [JsonPropertyName("requestedAgentName")]
    public string? RequestedAgentName { get; init; }

    [JsonPropertyName("sessionHash")]
    public string? SessionHash { get; init; }

    [JsonPropertyName("promptLength")]
    public int PromptLength { get; init; }

    [JsonPropertyName("promptPreview")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PromptPreview { get; init; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("responseLength")]
    public int ResponseLength { get; init; }

    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; init; }

    [JsonPropertyName("prompt_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? PromptTokens { get; init; }

    [JsonPropertyName("completion_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CompletionTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalTokens { get; init; }

    [JsonPropertyName("estimated_cost")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? EstimatedCost { get; init; }

    [JsonPropertyName("estimated_cost_currency")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EstimatedCostCurrency { get; init; }

    [JsonPropertyName("requiresApproval")]
    public bool RequiresApproval { get; init; }

    [JsonPropertyName("requiresClarification")]
    public bool RequiresClarification { get; init; }

    [JsonPropertyName("plannedActionCount")]
    public int PlannedActionCount { get; init; }

    [JsonPropertyName("executionResultCount")]
    public int ExecutionResultCount { get; init; }

    [JsonPropertyName("failedExecutionCount")]
    public int FailedExecutionCount { get; init; }

    [JsonPropertyName("errorType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorType { get; init; }

    [JsonPropertyName("errorMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; init; }
}
