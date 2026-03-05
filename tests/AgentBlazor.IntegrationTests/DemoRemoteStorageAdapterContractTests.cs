using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentBlazor.Demo.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentBlazor.IntegrationTests;

public class DemoRemoteStorageAdapterContractTests
{
    [Fact]
    public async Task HandoffAsync_HttpMode_PostsExpectedPayloadAndParsesToken()
    {
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    storageToken = "remote://ext/abc123",
                    message = "Stored remotely."
                })
            };
            return Task.FromResult(response);
        });
        var adapter = CreateHttpAdapter(handler);

        var result = await adapter.HandoffAsync("session-42", "invoice.pdf");

        Assert.True(result.Succeeded);
        Assert.Equal("remote://ext/abc123", result.StorageToken);
        Assert.Equal("Stored remotely.", result.Message);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://adapter.local/handoff", request.RequestUri);
        Assert.Equal("demo-api-key", request.ApiKey);
        Assert.Null(request.Authorization);

        var payload = JsonDocument.Parse(request.Body).RootElement;
        Assert.Equal("session-42", payload.GetProperty("sessionKey").GetString());
        Assert.Equal("invoice.pdf", payload.GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task ValidateTokenAsync_HttpMode_PostsExpectedPayloadAndParsesValidity()
    {
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    isValid = true,
                    message = "Token verified by upstream."
                })
            };
            return Task.FromResult(response);
        });
        var adapter = CreateHttpAdapter(handler);

        var result = await adapter.ValidateTokenAsync("session-9", "notes.md", "remote://ext/tkn-9");

        Assert.True(result.RequestSucceeded);
        Assert.True(result.IsValid);
        Assert.Equal("Token verified by upstream.", result.Message);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://adapter.local/validate", request.RequestUri);
        var payload = JsonDocument.Parse(request.Body).RootElement;
        Assert.Equal("session-9", payload.GetProperty("sessionKey").GetString());
        Assert.Equal("notes.md", payload.GetProperty("fileName").GetString());
        Assert.Equal("remote://ext/tkn-9", payload.GetProperty("storageToken").GetString());
    }

    [Fact]
    public async Task HandoffAsync_Http503_MapsToTransientFailure()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = JsonContent.Create(new
                {
                    message = "Temporary upstream outage."
                })
            }));
        var adapter = CreateHttpAdapter(handler);

        var result = await adapter.HandoffAsync("session-42", "invoice.pdf");

        Assert.False(result.Succeeded);
        Assert.True(result.IsTransientFailure);
        Assert.Equal("Temporary upstream outage.", result.Message);
    }

    [Fact]
    public async Task HandoffAsync_HttpMode_UsesBearerAndCustomPath()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    storageToken = "remote://ext/custom",
                    message = "Stored."
                })
            }));
        var adapter = CreateHttpAdapter(handler, options =>
        {
            options.HttpApiKey = null;
            options.HttpBearerToken = "demo-bearer";
            options.HttpHandoffPath = "/v1/files/handoff";
        });

        var result = await adapter.HandoffAsync("session-abc", "design.png");

        Assert.True(result.Succeeded);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://adapter.local/v1/files/handoff", request.RequestUri);
        Assert.Null(request.ApiKey);
        Assert.Equal("Bearer demo-bearer", request.Authorization);
    }

    [Fact]
    public async Task ValidateTokenAsync_Http429_MapsToTransientFailure()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = JsonContent.Create(new
                {
                    message = "Rate limited."
                })
            }));
        var adapter = CreateHttpAdapter(handler);

        var result = await adapter.ValidateTokenAsync("session-9", "notes.md", "remote://ext/tkn-9");

        Assert.False(result.RequestSucceeded);
        Assert.False(result.IsValid);
        Assert.True(result.IsTransientFailure);
        Assert.Equal("Rate limited.", result.Message);
    }

    [Fact]
    public async Task ValidateTokenAsync_Http400_ExtractsNestedErrorMessageAndIsNotTransient()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new
                {
                    error = new
                    {
                        message = "Invalid token format."
                    }
                })
            }));
        var adapter = CreateHttpAdapter(handler, options =>
        {
            options.HttpValidatePath = "/v1/files/validate";
        });

        var result = await adapter.ValidateTokenAsync("session-9", "notes.md", "broken-token");

        Assert.False(result.RequestSucceeded);
        Assert.False(result.IsTransientFailure);
        Assert.Equal("Invalid token format.", result.Message);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://adapter.local/v1/files/validate", request.RequestUri);
    }

    private static DemoRemoteStorageAdapter CreateHttpAdapter(
        HttpMessageHandler handler,
        Action<DemoRemoteStorageOptions>? configure = null)
    {
        var factory = new SingleHttpClientFactory(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://adapter.local/")
        });
        var configuredOptions = new DemoRemoteStorageOptions
        {
            Adapter = "Http",
            HttpBaseUrl = "https://adapter.local/",
            HttpApiKey = "demo-api-key",
            HttpBearerToken = null,
            HttpHandoffPath = "handoff",
            HttpValidatePath = "validate",
            MaxAttempts = 3,
            RetryDelayMilliseconds = 50
        };
        configure?.Invoke(configuredOptions);
        var options = Microsoft.Extensions.Options.Options.Create(configuredOptions);
        return new DemoRemoteStorageAdapter(factory, options, NullLogger<DemoRemoteStorageAdapter>.Instance);
    }

    private sealed class SingleHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var apiKey = request.Headers.TryGetValues("X-Api-Key", out var values)
                ? values.FirstOrDefault()
                : null;
            var authorization = request.Headers.TryGetValues("Authorization", out var authValues)
                ? authValues.FirstOrDefault()
                : null;
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                apiKey,
                authorization,
                body));
            return await responder(request, cancellationToken);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string RequestUri,
        string? ApiKey,
        string? Authorization,
        string Body);
}
