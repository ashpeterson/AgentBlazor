using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentBlazor.Core.Paid;

/// <summary>
/// Paid-tier proactive insight service. After each turn, calls the LLM to check whether
/// there is a genuinely useful observation worth surfacing to the user.
/// </summary>
public sealed class LlmProactiveInsightService : IProactiveInsightService
{
    private readonly IChatClient? _chatClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public LlmProactiveInsightService(IChatClient? chatClient = null)
    {
        _chatClient = chatClient;
    }

    public async Task<string?> GetInsightAsync(
        string sessionId,
        string lastUserMessage,
        IReadOnlyList<string> executedActionSummaries,
        string? componentContext,
        CancellationToken ct = default)
    {
        if (_chatClient is null || executedActionSummaries.Count == 0)
            return null;

        try
        {
            var actionsSummary = string.Join(", ", executedActionSummaries);
            var contextPart = string.IsNullOrWhiteSpace(componentContext)
                ? string.Empty
                : $"\nComponent context: {componentContext}";

            var prompt = $$"""
                The user just performed: {{actionsSummary}}.
                User message: {{lastUserMessage}}{{contextPart}}
                Is there a short, genuinely useful proactive insight (not obvious, not annoying)?
                Return: {"hasInsight": true, "message": "..."} or {"hasInsight": false}
                Be conservative — only return an insight if it's clearly valuable.
                """;

            var response = await _chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                new ChatOptions { Temperature = 0.3f, MaxOutputTokens = 100 },
                ct);

            var text = response.Text?.Trim() ?? string.Empty;

            // Extract JSON object from response
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
                return null;

            var json = text[start..(end + 1)];
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("hasInsight", out var hasInsight) || !hasInsight.GetBoolean())
                return null;

            if (!root.TryGetProperty("message", out var message))
                return null;

            var insight = message.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(insight) ? null : insight;
        }
        catch
        {
            return null;
        }
    }
}
