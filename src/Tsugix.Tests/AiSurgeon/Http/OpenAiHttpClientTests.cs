using System.Net;
using System.Text.Json;
using Tsugix.AiSurgeon;
using Tsugix.AiSurgeon.Http;
using Xunit;

namespace Tsugix.Tests.AiSurgeon.Http;

/// <summary>
/// Tests for OpenAI HTTP client.
/// Uses mock HttpMessageHandler - no real network calls.
/// </summary>
public class OpenAiHttpClientTests
{
    [Fact]
    public async Task CompleteAsync_ValidResponse_ReturnsContent()
    {
        var responseJson = """
            {
                "choices": [
                    {
                        "message": {
                            "role": "assistant",
                            "content": "Hello, world!"
                        },
                        "finish_reason": "stop"
                    }
                ]
            }
            """;
        
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/chat/completions") };
        
        using var client = new OpenAiHttpClient(httpClient, "gpt-4o", ownsHttpClient: false);
        
        var result = await client.CompleteAsync(
            "You are a helpful assistant.",
            "Say hello",
            new LlmOptions { TimeoutSeconds = 30 });
        
        Assert.Equal("Hello, world!", result);
    }
    
    [Fact]
    public async Task CompleteAsync_SendsCorrectRequestBody()
    {
        var responseJson = """{"choices":[{"message":{"role":"assistant","content":"ok"}}]}""";
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/chat/completions") };
        
        using var client = new OpenAiHttpClient(httpClient, "gpt-4o-mini", ownsHttpClient: false);
        
        await client.CompleteAsync(
            "System prompt",
            "User prompt",
            new LlmOptions { MaxTokens = 1000, Temperature = 0.5 });
        
        Assert.NotNull(handler.LastRequestContent);
        var request = JsonDocument.Parse(handler.LastRequestContent);
        
        Assert.Equal("gpt-4o-mini", request.RootElement.GetProperty("model").GetString());
        Assert.Equal(1000, request.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.5, request.RootElement.GetProperty("temperature").GetDouble());
        
        var messages = request.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("System prompt", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("User prompt", messages[1].GetProperty("content").GetString());
    }
    
    [Fact]
    public async Task CompleteAsync_ApiError_ThrowsHttpRequestException()
    {
        var errorJson = """{"error":{"message":"Invalid API key","type":"invalid_request_error"}}""";
        var handler = new MockHttpMessageHandler(errorJson, HttpStatusCode.Unauthorized);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/chat/completions") };
        
        using var client = new OpenAiHttpClient(httpClient, "gpt-4o", ownsHttpClient: false);
        
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CompleteAsync("sys", "user", new LlmOptions { RetryCount = 0 }));
        
        Assert.Contains("401", ex.Message);
    }
    
    [Fact]
    public async Task CompleteAsync_Timeout_ThrowsTimeoutException()
    {
        var handler = new MockHttpMessageHandler(delay: TimeSpan.FromSeconds(10));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/chat/completions") };
        
        using var client = new OpenAiHttpClient(httpClient, "gpt-4o", ownsHttpClient: false);
        
        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            client.CompleteAsync("sys", "user", new LlmOptions { TimeoutSeconds = 1, RetryCount = 0 }));
        
        Assert.Contains("timed out", ex.Message);
    }
    
    [Fact]
    public async Task CompleteAsync_RetryableError_RetriesWithBackoff()
    {
        var responseJson = """{"choices":[{"message":{"role":"assistant","content":"ok"}}]}""";
        var handler = new MockHttpMessageHandler(
            responseJson, 
            HttpStatusCode.OK,
            failFirstNRequests: 2,
            failStatusCode: HttpStatusCode.TooManyRequests);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/chat/completions") };
        
        using var client = new OpenAiHttpClient(httpClient, "gpt-4o", ownsHttpClient: false);
        
        var result = await client.CompleteAsync(
            "sys", "user", 
            new LlmOptions { RetryCount = 3, TimeoutSeconds = 30 });
        
        Assert.Equal("ok", result);
        Assert.Equal(3, handler.RequestCount); // 2 failures + 1 success
    }
    
    [Fact]
    public async Task CompleteAsync_EmptyResponse_ReturnsEmptyString()
    {
        var responseJson = """{"choices":[{"message":{"role":"assistant","content":""}}]}""";
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/chat/completions") };
        
        using var client = new OpenAiHttpClient(httpClient, "gpt-4o", ownsHttpClient: false);
        
        var result = await client.CompleteAsync("sys", "user", new LlmOptions());
        
        Assert.Equal(string.Empty, result);
    }
}

/// <summary>
/// Mock HTTP message handler for testing without network calls.
/// </summary>
internal class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseContent;
    private readonly HttpStatusCode _statusCode;
    private readonly TimeSpan? _delay;
    private readonly int _failFirstNRequests;
    private readonly HttpStatusCode _failStatusCode;
    
    public string? LastRequestContent { get; private set; }
    public int RequestCount { get; private set; }
    
    public MockHttpMessageHandler(
        string responseContent = "", 
        HttpStatusCode statusCode = HttpStatusCode.OK,
        TimeSpan? delay = null,
        int failFirstNRequests = 0,
        HttpStatusCode failStatusCode = HttpStatusCode.InternalServerError)
    {
        _responseContent = responseContent;
        _statusCode = statusCode;
        _delay = delay;
        _failFirstNRequests = failFirstNRequests;
        _failStatusCode = failStatusCode;
    }
    
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, 
        CancellationToken cancellationToken)
    {
        RequestCount++;
        
        if (request.Content != null)
        {
            LastRequestContent = await request.Content.ReadAsStringAsync(cancellationToken);
        }
        
        if (_delay.HasValue)
        {
            await Task.Delay(_delay.Value, cancellationToken);
        }
        
        if (RequestCount <= _failFirstNRequests)
        {
            return new HttpResponseMessage(_failStatusCode)
            {
                Content = new StringContent("{\"error\":{\"message\":\"Rate limited\"}}")
            };
        }
        
        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseContent)
        };
    }
}
