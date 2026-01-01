using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Tsugix.Logging;
using Tsugix.Telemetry;

namespace Tsugix.AiSurgeon.Http;

/// <summary>
/// Direct HTTP client for Anthropic Messages API.
/// AOT-friendly implementation with correct Anthropic request/response mapping.
/// </summary>
public sealed class AnthropicHttpClient : ILlmClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly bool _ownsHttpClient;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger<AnthropicHttpClient> _logger;
    
    private const string DefaultEndpoint = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";
    private const string ProviderName = "Anthropic";
    
    /// <summary>
    /// Creates a new Anthropic HTTP client.
    /// </summary>
    /// <param name="apiKey">The Anthropic API key.</param>
    /// <param name="model">The model name (e.g., "claude-3-5-sonnet-20241022").</param>
    /// <param name="endpoint">Optional custom endpoint URL.</param>
    /// <param name="rateLimiter">Optional rate limiter (uses default if not provided).</param>
    public AnthropicHttpClient(string apiKey, string model, string? endpoint = null, RateLimiter? rateLimiter = null)
        : this(CreateHttpClient(apiKey, endpoint), model, ownsHttpClient: true, rateLimiter)
    {
    }
    
    /// <summary>
    /// Creates a new Anthropic HTTP client with a custom HttpClient (for testing).
    /// </summary>
    public AnthropicHttpClient(HttpClient httpClient, string model, bool ownsHttpClient = false, RateLimiter? rateLimiter = null)
    {
        _httpClient = httpClient;
        _model = model;
        _ownsHttpClient = ownsHttpClient;
        _rateLimiter = rateLimiter ?? RateLimiter.Default;
        _logger = TsugixLogger.CreateLogger<AnthropicHttpClient>();
    }
    
    private static HttpClient CreateHttpClient(string apiKey, string? endpoint)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(endpoint ?? DefaultEndpoint)
        };
        // Anthropic uses x-api-key header, not Bearer token
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", ApiVersion);
        return client;
    }
    
    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        LlmOptions options,
        CancellationToken cancellationToken = default)
    {
        var request = new AnthropicRequest
        {
            Model = _model,
            System = systemPrompt,
            Messages = new[]
            {
                new AnthropicMessage { Role = "user", Content = userPrompt }
            },
            MaxTokens = options.MaxTokens
        };
        
        var attempt = 0;
        var maxAttempts = options.RetryCount + 1;
        Exception? lastException = null;
        
        // Acquire rate limit permit
        using var permit = await _rateLimiter.AcquireAsync(ProviderName, cancellationToken);
        
        while (attempt < maxAttempts)
        {
            attempt++;
            using var metricsScope = TsugixMetrics.Instance.StartLlmRequest(ProviderName, _model);
            
            try
            {
                _logger.LogDebug(LogEvents.LlmRequestStarted, 
                    "Anthropic request attempt {Attempt}/{MaxAttempts} to model {Model}", 
                    attempt, maxAttempts, _model);
                
                using var timeoutCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(options.TimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, timeoutCts.Token);
                
                var response = await SendRequestAsync(request, linkedCts.Token);
                
                metricsScope.SetResult(success: true, statusCode: 200);
                _logger.LogDebug(LogEvents.LlmRequestCompleted, "Anthropic request completed successfully");
                
                return response;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = new TimeoutException(
                    $"Anthropic API call timed out after {options.TimeoutSeconds} seconds");
                
                metricsScope.SetResult(success: false, statusCode: 408);
                _logger.LogWarning(LogEvents.LlmRequestRetry, 
                    "Anthropic request timed out, attempt {Attempt}/{MaxAttempts}", attempt, maxAttempts);
                
                if (attempt < maxAttempts)
                {
                    await DelayWithJitter(attempt, cancellationToken);
                }
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts && IsRetryable(ex))
            {
                lastException = ex;
                metricsScope.SetResult(success: false, statusCode: (int?)ex.StatusCode);
                _logger.LogWarning(LogEvents.LlmRequestRetry, 
                    "Anthropic request failed with {StatusCode}, attempt {Attempt}/{MaxAttempts}", 
                    ex.StatusCode, attempt, maxAttempts);
                
                await DelayWithJitter(attempt, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                metricsScope.SetResult(success: false, statusCode: (int?)ex.StatusCode);
                _logger.LogError(LogEvents.LlmRequestFailed, ex, 
                    "Anthropic request failed with {StatusCode}", ex.StatusCode);
                throw;
            }
        }
        
        _logger.LogError(LogEvents.LlmRequestFailed, lastException, 
            "Anthropic request failed after {MaxAttempts} attempts", maxAttempts);
        throw lastException ?? new InvalidOperationException("Anthropic call failed with no exception");
    }
    
    private async Task<string> SendRequestAsync(AnthropicRequest request, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, AnthropicJsonContext.Default.AnthropicRequest);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        
        using var response = await _httpClient.PostAsync("", content, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Anthropic API error {(int)response.StatusCode}: {errorBody}",
                null,
                response.StatusCode);
        }
        
        var result = await response.Content.ReadFromJsonAsync(
            AnthropicJsonContext.Default.AnthropicResponse, 
            cancellationToken);
        
        // Anthropic returns content as an array of content blocks
        var textContent = result?.Content?
            .Where(c => c.Type == "text")
            .Select(c => c.Text)
            .FirstOrDefault();
        
        return textContent ?? string.Empty;
    }
    
    private static bool IsRetryable(HttpRequestException ex)
    {
        return ex.StatusCode is 
            System.Net.HttpStatusCode.TooManyRequests or
            System.Net.HttpStatusCode.InternalServerError or
            System.Net.HttpStatusCode.BadGateway or
            System.Net.HttpStatusCode.ServiceUnavailable or
            System.Net.HttpStatusCode.GatewayTimeout;
    }
    
    private static async Task DelayWithJitter(int attempt, CancellationToken cancellationToken)
    {
        // Exponential backoff with jitter: base * 2^attempt + random(0-500ms)
        var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
        await Task.Delay(baseDelay + jitter, cancellationToken);
    }
    
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

#region Anthropic API Models

internal sealed class AnthropicRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }
    
    [JsonPropertyName("system")]
    public string? System { get; init; }
    
    [JsonPropertyName("messages")]
    public required AnthropicMessage[] Messages { get; init; }
    
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; }
}

internal sealed class AnthropicMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }
    
    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

internal sealed class AnthropicResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }
    
    [JsonPropertyName("type")]
    public string? Type { get; init; }
    
    [JsonPropertyName("role")]
    public string? Role { get; init; }
    
    [JsonPropertyName("content")]
    public AnthropicContentBlock[]? Content { get; init; }
    
    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; init; }
    
    [JsonPropertyName("error")]
    public AnthropicError? Error { get; init; }
}

internal sealed class AnthropicContentBlock
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }
    
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

internal sealed class AnthropicError
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }
    
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

#endregion

/// <summary>
/// JSON source generation context for Anthropic API models (AOT-compatible).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(AnthropicRequest))]
[JsonSerializable(typeof(AnthropicResponse))]
[JsonSerializable(typeof(AnthropicMessage))]
[JsonSerializable(typeof(AnthropicContentBlock))]
[JsonSerializable(typeof(AnthropicError))]
internal partial class AnthropicJsonContext : JsonSerializerContext
{
}
