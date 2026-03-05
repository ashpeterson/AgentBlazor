using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Demo.Services;

internal sealed class DemoRemoteStorageAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<DemoRemoteStorageOptions> options,
    ILogger<DemoRemoteStorageAdapter> logger) : IDemoRemoteStorageAdapter
{
    private readonly ConcurrentDictionary<string, StoredTokenRecord> _storedTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _inMemoryAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly DemoRemoteStorageOptions _options = options.Value;

    public string AdapterName =>
        ShouldUseHttpAdapter(_options)
            ? "HttpRemoteStorageAdapter"
            : "InMemoryRemoteStorageAdapter";

    public async Task<DemoRemoteStorageHandoffResult> HandoffAsync(
        string sessionKey,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (ShouldUseHttpAdapter(_options))
        {
            return await HandoffWithHttpAsync(sessionKey, fileName, cancellationToken);
        }

        return HandoffWithInMemoryAdapter(sessionKey, fileName);
    }

    public async Task<DemoRemoteStorageValidationResult> ValidateTokenAsync(
        string sessionKey,
        string fileName,
        string storageToken,
        CancellationToken cancellationToken = default)
    {
        if (ShouldUseHttpAdapter(_options))
        {
            return await ValidateWithHttpAsync(sessionKey, fileName, storageToken, cancellationToken);
        }

        return ValidateWithInMemoryAdapter(sessionKey, fileName, storageToken);
    }

    private static bool ShouldUseHttpAdapter(DemoRemoteStorageOptions options)
    {
        return string.Equals(options.Adapter, "Http", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(options.HttpBaseUrl);
    }

    private DemoRemoteStorageHandoffResult HandoffWithInMemoryAdapter(string sessionKey, string fileName)
    {
        // Deterministic transient failure simulation enables retry-path validation without external infra.
        if (fileName.Contains("-retry", StringComparison.OrdinalIgnoreCase))
        {
            var key = $"{sessionKey}::{fileName}";
            var attempt = _inMemoryAttempts.AddOrUpdate(key, 1, static (_, current) => current + 1);
            if (attempt == 1)
            {
                return new DemoRemoteStorageHandoffResult(
                    Succeeded: false,
                    IsTransientFailure: true,
                    StorageToken: null,
                    Message: $"Transient upload failure for '{fileName}'.");
            }
        }

        if (fileName.Contains("-reject", StringComparison.OrdinalIgnoreCase))
        {
            return new DemoRemoteStorageHandoffResult(
                Succeeded: false,
                IsTransientFailure: false,
                StorageToken: null,
                Message: $"Remote adapter rejected '{fileName}' due to policy.");
        }

        var token = BuildInMemoryToken(sessionKey, fileName);
        _storedTokens[token] = new StoredTokenRecord(sessionKey, fileName, DateTime.UtcNow);
        return new DemoRemoteStorageHandoffResult(
            Succeeded: true,
            IsTransientFailure: false,
            StorageToken: token,
            Message: $"Stored '{fileName}' in in-memory remote adapter.");
    }

    private DemoRemoteStorageValidationResult ValidateWithInMemoryAdapter(
        string sessionKey,
        string fileName,
        string storageToken)
    {
        if (!_storedTokens.TryGetValue(storageToken, out var record))
        {
            return new DemoRemoteStorageValidationResult(
                RequestSucceeded: true,
                IsTransientFailure: false,
                IsValid: false,
                Message: $"Token '{storageToken}' was not found.");
        }

        var matchesSession = string.Equals(record.SessionKey, sessionKey, StringComparison.OrdinalIgnoreCase);
        var matchesFile = string.Equals(record.FileName, fileName, StringComparison.OrdinalIgnoreCase);
        if (matchesSession && matchesFile)
        {
            return new DemoRemoteStorageValidationResult(
                RequestSucceeded: true,
                IsTransientFailure: false,
                IsValid: true,
                Message: $"Token '{storageToken}' verified.");
        }

        return new DemoRemoteStorageValidationResult(
            RequestSucceeded: true,
            IsTransientFailure: false,
            IsValid: false,
            Message: $"Token '{storageToken}' does not match session/file ownership.");
    }

    private async Task<DemoRemoteStorageHandoffResult> HandoffWithHttpAsync(
        string sessionKey,
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendHttpAsync(
                "handoff",
                new HttpHandoffRequest(sessionKey, fileName),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var (message, isTransient) = await ReadHttpErrorAsync(response, cancellationToken);
                return new DemoRemoteStorageHandoffResult(
                    Succeeded: false,
                    IsTransientFailure: isTransient,
                    StorageToken: null,
                    Message: message);
            }

            var payload = await response.Content.ReadFromJsonAsync<HttpHandoffResponse>(cancellationToken);
            if (string.IsNullOrWhiteSpace(payload?.StorageToken))
            {
                return new DemoRemoteStorageHandoffResult(
                    Succeeded: false,
                    IsTransientFailure: false,
                    StorageToken: null,
                    Message: "HTTP adapter response did not include a storage token.");
            }

            return new DemoRemoteStorageHandoffResult(
                Succeeded: true,
                IsTransientFailure: false,
                StorageToken: payload.StorageToken.Trim(),
                Message: string.IsNullOrWhiteSpace(payload.Message)
                    ? $"Stored '{fileName}' via HTTP remote adapter."
                    : payload.Message.Trim());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "HTTP handoff failed for {FileName}.", fileName);
            return new DemoRemoteStorageHandoffResult(
                Succeeded: false,
                IsTransientFailure: true,
                StorageToken: null,
                Message: $"HTTP handoff failed for '{fileName}': {ex.Message}");
        }
    }

    private async Task<DemoRemoteStorageValidationResult> ValidateWithHttpAsync(
        string sessionKey,
        string fileName,
        string storageToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendHttpAsync(
                "validate",
                new HttpValidateRequest(sessionKey, fileName, storageToken),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var (message, isTransient) = await ReadHttpErrorAsync(response, cancellationToken);
                return new DemoRemoteStorageValidationResult(
                    RequestSucceeded: false,
                    IsTransientFailure: isTransient,
                    IsValid: false,
                    Message: message);
            }

            var payload = await response.Content.ReadFromJsonAsync<HttpValidateResponse>(cancellationToken);
            if (payload is null)
            {
                return new DemoRemoteStorageValidationResult(
                    RequestSucceeded: false,
                    IsTransientFailure: false,
                    IsValid: false,
                    Message: "HTTP adapter returned an empty token validation response.");
            }

            return new DemoRemoteStorageValidationResult(
                RequestSucceeded: true,
                IsTransientFailure: false,
                IsValid: payload.IsValid,
                Message: string.IsNullOrWhiteSpace(payload.Message)
                    ? (payload.IsValid ? "Token verified by HTTP adapter." : "Token rejected by HTTP adapter.")
                    : payload.Message.Trim());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "HTTP token validation failed for {FileName}.", fileName);
            return new DemoRemoteStorageValidationResult(
                RequestSucceeded: false,
                IsTransientFailure: true,
                IsValid: false,
                Message: $"HTTP token validation failed for '{fileName}': {ex.Message}");
        }
    }

    private async Task<HttpResponseMessage> SendHttpAsync(
        string operation,
        object payload,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("demo-remote-storage");
        client.BaseAddress = new Uri(_options.HttpBaseUrl!, UriKind.Absolute);
        if (!string.IsNullOrWhiteSpace(_options.HttpApiKey))
        {
            client.DefaultRequestHeaders.Remove("X-Api-Key");
            client.DefaultRequestHeaders.Add("X-Api-Key", _options.HttpApiKey.Trim());
        }

        if (!string.IsNullOrWhiteSpace(_options.HttpBearerToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                _options.HttpBearerToken.Trim());
        }
        else
        {
            client.DefaultRequestHeaders.Authorization = null;
        }

        var path = ResolveHttpPath(operation);
        return await client.PostAsJsonAsync(path, payload, cancellationToken);
    }

    private string ResolveHttpPath(string operation)
    {
        var configured = string.Equals(operation, "validate", StringComparison.OrdinalIgnoreCase)
            ? _options.HttpValidatePath
            : _options.HttpHandoffPath;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return operation;
        }

        return configured.TrimStart('/');
    }

    private static async Task<(string Message, bool IsTransientFailure)> ReadHttpErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var fallbackMessage = $"HTTP adapter returned {(int)response.StatusCode} ({response.StatusCode}).";
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (TryReadErrorMessage(document.RootElement, out var parsedMessage))
                {
                    fallbackMessage = parsedMessage;
                }
            }
            catch (JsonException)
            {
                // Keep fallback when the response body is not JSON.
            }
        }

        var isTransient = response.StatusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            || (int)response.StatusCode >= 500;

        return (fallbackMessage, isTransient);
    }

    private static bool TryReadErrorMessage(JsonElement root, out string message)
    {
        message = string.Empty;
        if (TryReadStringProperty(root, "message", out message) ||
            TryReadStringProperty(root, "detail", out message))
        {
            return true;
        }

        if (!root.TryGetProperty("error", out var error))
        {
            return false;
        }

        return error.ValueKind switch
        {
            JsonValueKind.String => TryReadStringValue(error, out message),
            JsonValueKind.Object => TryReadStringProperty(error, "message", out message)
                                     || TryReadStringProperty(error, "detail", out message),
            _ => false
        };
    }

    private static bool TryReadStringProperty(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(propertyName, out var property)
               && TryReadStringValue(property, out value);
    }

    private static bool TryReadStringValue(JsonElement element, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var resolved = element.GetString();
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return false;
        }

        value = resolved.Trim();
        return true;
    }

    private static string BuildInMemoryToken(string sessionKey, string fileName)
    {
        var safeSession = ToSafeIdentifier(sessionKey);
        var safeFile = ToSafeIdentifier(fileName);
        return $"remote://mem/{safeSession}/{safeFile}/{Guid.NewGuid():N}";
    }

    private static string ToSafeIdentifier(string value)
    {
        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(static ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());
    }

    private sealed record StoredTokenRecord(
        string SessionKey,
        string FileName,
        DateTime StoredUtc);

    private sealed record HttpHandoffRequest(
        string SessionKey,
        string FileName);

    private sealed record HttpHandoffResponse(
        string? StorageToken,
        string? Message);

    private sealed record HttpValidateRequest(
        string SessionKey,
        string FileName,
        string StorageToken);

    private sealed record HttpValidateResponse(
        bool IsValid,
        string? Message);
}
