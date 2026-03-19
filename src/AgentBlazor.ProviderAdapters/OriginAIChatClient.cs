using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentBlazor.ProviderAdapters;

internal sealed class OriginAIChatClient(
    HttpClient httpClient,
    string chatSource,
    string? systemPrompt) : IChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = httpClient;
    private readonly string _chatSource = string.IsNullOrWhiteSpace(chatSource) ? "agentblazor" : chatSource;
    private readonly string? _systemPrompt = string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder();
        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(update.Text))
            {
                builder.Append(update.Text);
            }
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, builder.ToString()));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var transcript = BuildTranscript(messages);
        var session = await StartSessionAsync(messages, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/send/stream")
        {
            Content = JsonContent.Create(
                new OriginAiChatSendRequest
                {
                    ChatId = session.ChatId,
                    Message = transcript
                },
                options: JsonOptions)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"OriginAI chat stream failed with HTTP {(int)response.StatusCode}: {error}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? eventName = null;
        var dataBuilder = new StringBuilder();
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                if (dataBuilder.Length > 0)
                {
                    var data = dataBuilder.ToString().TrimEnd('\n');
                    dataBuilder.Clear();

                    if (string.Equals(eventName, "error", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(data);
                    }

                    if (string.Equals(eventName, "chunk", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(data))
                    {
                        yield return new ChatResponseUpdate(ChatRole.Assistant, data);
                    }

                    if (string.Equals(eventName, "done", StringComparison.OrdinalIgnoreCase))
                    {
                        yield break;
                    }
                }

                eventName = null;
                continue;
            }

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line["event:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var dataPart = line["data:".Length..];
                if (dataPart.StartsWith(" ", StringComparison.Ordinal))
                {
                    dataPart = dataPart[1..];
                }

                dataBuilder.Append(dataPart).Append('\n');
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        _ = serviceKey;
        return serviceType == typeof(HttpClient) ? _httpClient : null;
    }

    public void Dispose()
    {
    }

    private async Task<OriginAiChatSession> StartSessionAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var systemPrompt = ResolveSystemPrompt(messages);
        using var response = await _httpClient.PostAsJsonAsync(
            "/api/v1/chat/start",
            new OriginAiChatStartRequest
            {
                ChatSource = _chatSource,
                SystemPrompt = systemPrompt
            },
            JsonOptions,
            cancellationToken);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        var session = JsonSerializer.Deserialize<OriginAiChatSession>(payload, JsonOptions);
        return session ?? throw new InvalidOperationException(
            $"Failed to deserialize OriginAI chat session: {payload}");
    }

    private string ResolveSystemPrompt(IEnumerable<ChatMessage> messages)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(_systemPrompt))
        {
            builder.AppendLine(_systemPrompt);
        }

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                var text = ExtractText(message);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (builder.Length > 0)
                    {
                        builder.AppendLine().AppendLine();
                    }

                    builder.Append(text);
                }
            }
        }

        return builder.ToString();
    }

    private static string BuildTranscript(IEnumerable<ChatMessage> messages)
    {
        var relevantMessages = messages
            .Where(static message => message.Role != ChatRole.System)
            .ToList();
        if (relevantMessages.Count == 0)
        {
            return "Continue.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("Conversation transcript:");
        foreach (var message in relevantMessages)
        {
            var role = message.Role == ChatRole.Assistant ? "Assistant" : "User";
            var text = ExtractText(message);
            if (!string.IsNullOrWhiteSpace(text))
            {
                builder.Append(role).Append(": ").AppendLine(text);
            }
        }

        builder.AppendLine();
        builder.Append("Reply as the assistant to the latest user request.");
        return builder.ToString();
    }

    private static string ExtractText(ChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            return message.Text;
        }

        var builder = new StringBuilder();
        foreach (var content in message.Contents.OfType<TextContent>())
        {
            if (!string.IsNullOrWhiteSpace(content.Text))
            {
                builder.Append(content.Text);
            }
        }

        return builder.ToString();
    }

    private sealed class OriginAiChatStartRequest
    {
        public string? SystemPrompt { get; set; }

        public string? ChatSource { get; set; }
    }

    private sealed class OriginAiChatSendRequest
    {
        public string ChatId { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
    }

    private sealed class OriginAiChatSession
    {
        public string ChatId { get; set; } = string.Empty;
    }
}
