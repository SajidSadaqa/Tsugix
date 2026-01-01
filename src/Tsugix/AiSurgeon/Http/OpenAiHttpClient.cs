using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Tsugix.Logging;
using Tsugix.Telemetry;

namespace Tsugix.AiSurgeon.Http;

/// <summary>
/// Direct HTTP client for OpenAI Chat Completions API.
/// AOT-friendly implementation without Semantic Kernel dependencies.
/// </summary>
public sealed class OpenAiHttpClient : ILlmClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly bool _ownsHttpClient;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger<OpenAiHttpClient> _logger;
    
    private const string DefaultEndpoint = "https://api.openai.com/v1/chat/completions";
    private const string ProviderName = "OpenAI";
    
    /// <summary>
    /// Creates a new OpenAI HTTP client.
    /// </summary>
    /// <param name="apiKey">The OpenAI API key.</param>
    /// <param name="model">The model name (e.g., "gpt-4o").</param>
    /// <param name="endpoint">Optional custom endpoint URL.</param>
    /// <param name="rateLimiter">Optional rate limiter (uses default if not provided).</param>
    public OpenAiHttpClient(string apiKey, string model, string? endpoint = null, RateLimiter? rateLimiter = null)
        : this(CreateHttpClient(apiKey, endpoint), model, ownsHttpClient: true, rateLimiter)
    {
    }
    
    /// <summary>
    /// Creates a new OpenAI HTTP client with a custom HttpClient (for testing).
    /// </summary>
    public OpenAiHttpClient(HttpClient httpClient, string model, bool ownsHttpClient = false, RateLimiter? rateLimiter = null)
    {
        _httpClient = httpClient;
        _model = model;
        _ownsHttpClient = ownsHttpClient;
        _rateLimiter = rateLimiter ?? RateLimiter.Default;
        _logger = TsugixLogger.CreateLogger<OpenAiHttpClient>();
    }
    
    private static HttpClient CreateHttpClient(string apiKey, string? endpoint)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(endpoint ?? DefaultEndpoint)
        };
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        return client;
    }
    
    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        LlmOptions options,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenAiRequest
        {
            Model = _model,
            Messages = new[]
            {
                new OpenAiMessage { Role = "system", Content = systemPrompt },
                new OpenAiMessage { Role = "user", Content = userPrompt }
            },
            MaxTokens = options.MaxTokens,
            Temperature = options.Temperature
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
                    "OpenAI request attempt {Attempt}/{MaxAttempts} to model {Model}", 
                    attempt, maxAttempts, _model);
                
                using var timeoutCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(options.TimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, timeoutCts.Token);
                
                var response = await SendRequestAsync(request, linkedCts.Token);
                
                metricsScope.SetResult(success: true, statusCode: 200);
                _logger.LogDebug(LogEvents.LlmRequestCompleted, "OpenAI request completed successfully");
                
                return response;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = new TimeoutException(
                    $"OpenAI API call timed out after {options.TimeoutSeconds} seconds");
                
                metricsScope.SetResult(success: false, statusCode: 408);
                _logger.LogWarning(LogEvents.LlmRequestRetry, 
                    "OpenAI request timed out, attempt {Attempt}/{MaxAttempts}", attempt, maxAttempts);
                
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
                    "OpenAI request failed with {StatusCode}, attempt {Attempt}/{MaxAttempts}", 
                    ex.StatusCode, attempt, maxAttempts);
                
                await DelayWithJitter(attempt, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                metricsScope.SetResult(success: false, statusCode: (int?)ex.StatusCode);
                _logger.LogError(LogEvents.LlmRequestFailed, ex, 
                    "OpenAI request failed with {StatusCode}", ex.StatusCode);
                throw;
            }
        }
        
        _logger.LogError(LogEvents.LlmRequestFailed, lastException, 
            "OpenAI request failed after {MaxAttempts} attempts", maxAttempts);
        throw lastException ?? new InvalidOperationException("OpenAI call failed with no exception");
    }
    
    private async Task<string> SendRequestAsync(OpenAiRequest request, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, OpenAiJsonContext.Default.OpenAiRequest);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        
        using var response = await _httpClient.PostAsync("", content, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"OpenAI API error {(int)response.StatusCode}: {errorBody}",
                null,
                response.StatusCode);
        }
        
        var result = await response.Content.ReadFromJsonAsync(
            OpenAiJsonContext.Default.OpenAiResponse, 
            cancellationToken);
        
        return result?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
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

#region OpenAI API Models

internal sealed class OpenAiRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }
    
    [JsonPropertyName("messages")]
    public required OpenAiMessage[] Messages { get; init; }
    
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; }
    
    [JsonPropertyName("temperature")]
    public double Temperature { get; init; }
}

internal sealed class OpenAiMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }
    
    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

internal sealed class OpenAiResponse
{
    [JsonPropertyName("choices")]
    public OpenAiChoice[]? Choices { get; init; }
    
    [JsonPropertyName("error")]
    public OpenAiError? Error { get; init; }
}

internal sealed class OpenAiChoice
{
    [JsonPropertyName("message")]
    public OpenAiMessage? Message { get; init; }
    
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

internal sealed class OpenAiError
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }
    
    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

#endregion

/// <summary>
/// JSON source generation context for OpenAI API models (AOT-compatible).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OpenAiRequest))]
[JsonSerializable(typeof(OpenAiResponse))]
[JsonSerializable(typeof(OpenAiMessage))]
[JsonSerializable(typeof(OpenAiChoice))]
[JsonSerializable(typeof(OpenAiError))]
internal partial class OpenAiJsonContext : JsonSerializerContext
{
}
