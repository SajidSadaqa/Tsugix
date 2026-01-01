using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Tsugix.AiSurgeon;

/// <summary>
/// LLM client implementation using Microsoft Semantic Kernel.
/// Supports OpenAI and Anthropic providers.
/// </summary>
public sealed class SemanticKernelLlmClient : ILlmClient
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly LlmProvider _provider;
    
    /// <summary>
    /// Creates a new Semantic Kernel LLM client.
    /// </summary>
    /// <param name="provider">The LLM provider to use.</param>
    /// <param name="modelName">The model name (e.g., "gpt-4o", "claude-3-5-sonnet").</param>
    /// <param name="apiKey">The API key for the provider.</param>
    /// <param name="endpoint">Optional custom endpoint URL.</param>
    public SemanticKernelLlmClient(LlmProvider provider, string modelName, string apiKey, Uri? endpoint = null)
    {
        _provider = provider;
        
        var builder = Kernel.CreateBuilder();
        
        switch (provider)
        {
            case LlmProvider.OpenAI:
                builder.AddOpenAIChatCompletion(modelName, apiKey);
                break;
            case LlmProvider.Anthropic:
                // For Anthropic, we use a custom endpoint if provided
                // Note: Full Anthropic support requires Microsoft.SemanticKernel.Connectors.Anthropic
                // For now, this supports OpenAI-compatible endpoints
                if (endpoint != null)
                {
                    builder.AddOpenAIChatCompletion(
                        modelId: modelName,
                        apiKey: apiKey,
                        endpoint: endpoint);
                }
                else
                {
                    // Without a custom endpoint, use OpenAI connector directly
                    builder.AddOpenAIChatCompletion(modelName, apiKey);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider));
        }
        
        _kernel = builder.Build();
        _chatService = _kernel.GetRequiredService<IChatCompletionService>();
    }
    
    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        LlmOptions options,
        CancellationToken cancellationToken = default)
    {
        var history = new ChatHistory();
        history.AddSystemMessage(systemPrompt);
        history.AddUserMessage(userPrompt);
        
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = options.MaxTokens,
            Temperature = options.Temperature
        };
        
        var attempt = 0;
        var maxAttempts = options.RetryCount + 1;
        Exception? lastException = null;
        
        while (attempt < maxAttempts)
        {
            attempt++;
            
            try
            {
                using var timeoutCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(options.TimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, timeoutCts.Token);
                
                var response = await _chatService.GetChatMessageContentAsync(
                    history,
                    executionSettings,
                    _kernel,
                    linkedCts.Token);
                
                return response.Content ?? string.Empty;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout occurred, retry if attempts remain
                lastException = new TimeoutException(
                    $"LLM API call timed out after {options.TimeoutSeconds} seconds");
                
                if (attempt < maxAttempts)
                {
                    // Exponential backoff: 1s, 2s, 4s, etc.
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (Exception ex) when (attempt < maxAttempts && IsRetryableException(ex))
            {
                lastException = ex;
                
                // Exponential backoff
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                await Task.Delay(delay, cancellationToken);
            }
        }
        
        throw lastException ?? new InvalidOperationException("LLM call failed with no exception");
    }
    
    private static bool IsRetryableException(Exception ex)
    {
        // Retry on transient errors (rate limiting, server errors)
        return ex.Message.Contains("429") || // Rate limited
               ex.Message.Contains("500") || // Server error
               ex.Message.Contains("502") || // Bad gateway
               ex.Message.Contains("503") || // Service unavailable
               ex.Message.Contains("504");   // Gateway timeout
    }
}
