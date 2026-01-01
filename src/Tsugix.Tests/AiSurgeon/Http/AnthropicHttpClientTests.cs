using System.Net;
using System.Text.Json;
using Tsugix.AiSurgeon;
using Tsugix.AiSurgeon.Http;
using Xunit;

namespace Tsugix.Tests.AiSurgeon.Http;

/// <summary>
/// Tests for Anthropic HTTP client.
/// Uses mock HttpMessageHandler - no real network calls.
/// </summary>
public class AnthropicHttpClientTests
{
    [Fact]
    public async Task CompleteAsync_ValidResponse_ReturnsContent()
    {
        var responseJson = """
            {
                "id": "msg_123",
                "type": "message",
                "role": "assistant",
                "content": [
                    {
                        "type": "text",
                        "text": "Hello from Claude!"
                    }
                ],
                "stop_reason": "end_turn"
            }
            """;
        
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/v1/messages") };
        
        using var client = new AnthropicHttpClient(httpClient, "claude-3-5-sonnet-20241022", ownsHttpClient: false);
        
        var result = await client.CompleteAsync(
            "You are a helpful assistant.",
            "Say hello",
            new LlmOptions { TimeoutSeconds = 30 });
        
        Assert.Equal("Hello from Claude!", result);
    }
    
    [Fact]
    public async Task CompleteAsync_SendsCorrectRequestBody()
    {
        var responseJson = """{"content":[{"type":"text","text":"ok"}]}""";
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/v1/messages") };
        
        using var client = new AnthropicHttpClient(httpClient, "claude-3-haiku-20240307", ownsHttpClient: false);
        
        await client.CompleteAsync(
            "System prompt",
            "User prompt",
            new LlmOptions { MaxTokens = 2000 });
        
        Assert.NotNull(handler.LastRequestContent);
        var request = JsonDocument.Parse(handler.LastRequestContent);
        
        // Anthropic uses snake_case
        Assert.Equal("claude-3-haiku-20240307", request.RootElement.GetProperty("model").GetString());
        Assert.Equal(2000, request.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal("System prompt", request.RootElement.GetProperty("system").GetString());
        
        var messages = request.RootElement.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength()); // Only user message, system is separate
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("User prompt", messages[0].GetProperty("content").GetString());
    }
    
    [Fact]
    public async Task CompleteAsync_ApiError_ThrowsHttpRequestException()
    {
        var errorJson = """{"error":{"type":"authentication_error","message":"Invalid API key"}}""";
        var handler = new MockHttpMessageHandler(errorJson, HttpStatusCode.Unauthorized);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/v1/messages") };
        
        using var client = new AnthropicHttpClient(httpClient, "claude-3-5-sonnet-20241022", ownsHttpClient: false);
        
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CompleteAsync("sys", "user", new LlmOptions { RetryCount = 0 }));
        
        Assert.Contains("401", ex.Message);
    }
    
    [Fact]
    public async Task CompleteAsync_Timeout_ThrowsTimeoutException()
    {
        var handler = new MockHttpMessageHandler(delay: TimeSpan.FromSeconds(10));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/v1/messages") };
        
        using var client = new AnthropicHttpClient(httpClient, "claude-3-5-sonnet-20241022", ownsHttpClient: false);
        
        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            client.CompleteAsync("sys", "user", new LlmOptions { TimeoutSeconds = 1, RetryCount = 0 }));
        
        Assert.Contains("timed out", ex.Message);
    }
    
    [Fact]
    public async Task CompleteAsync_RetryableError_RetriesWithBackoff()
    {
        var responseJson = """{"content":[{"type":"text","text":"success"}]}""";
        var handler = new MockHttpMessageHandler(
            responseJson, 
            HttpStatusCode.OK,
            failFirstNRequests: 1,
            failStatusCode: HttpStatusCode.ServiceUnavailable);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/v1/messages") };
        
        using var client = new AnthropicHttpClient(httpClient, "claude-3-5-sonnet-20241022", ownsHttpClient: false);
        
        var result = await client.CompleteAsync(
            "sys", "user", 
            new LlmOptions { RetryCount = 2, TimeoutSeconds = 30 });
        
        Assert.Equal("success", result);
        Assert.Equal(2, handler.RequestCount); // 1 failure + 1 success
    }
    
    [Fact]
    public async Task CompleteAsync_MultipleContentBlocks_ReturnsFirstText()
    {
        var responseJson = """
            {
                "content": [
                    {"type": "text", "text": "First block"},
                    {"type": "text", "text": "Second block"}
                ]
            }
            """;
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/v1/messages") };
        
        using var client = new AnthropicHttpClient(httpClient, "claude-3-5-sonnet-20241022", ownsHttpClient: false);
        
        var result = await client.CompleteAsync("sys", "user", new LlmOptions());
        
        Assert.Equal("First block", result);
    }
    
    [Fact]
    public async Task CompleteAsync_EmptyContent_ReturnsEmptyString()
    {
        var responseJson = """{"content":[]}""";
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/v1/messages") };
        
        using var client = new AnthropicHttpClient(httpClient, "claude-3-5-sonnet-20241022", ownsHttpClient: false);
        
        var result = await client.CompleteAsync("sys", "user", new LlmOptions());
        
        Assert.Equal(string.Empty, result);
    }
}
