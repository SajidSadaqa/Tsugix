using Tsugix.AiSurgeon.Http;

namespace Tsugix.AiSurgeon;

/// <summary>
/// Factory for creating LLM clients based on configuration.
/// Default: Uses direct HTTP clients (AOT-friendly).
/// Optional: Can use Semantic Kernel if explicitly enabled.
/// </summary>
public static class LlmClientFactory
{
    /// <summary>
    /// Creates an LLM client based on the provided configuration.
    /// Uses direct HTTP clients by default for AOT compatibility.
    /// </summary>
    /// <param name="config">The Tsugix configuration.</param>
    /// <param name="configManager">The configuration manager for API key retrieval.</param>
    /// <param name="useSemanticKernel">If true, uses Semantic Kernel (not AOT-friendly).</param>
    /// <param name="rateLimiter">Optional rate limiter (uses default if not provided).</param>
    /// <returns>A configured LLM client.</returns>
    /// <exception cref="InvalidOperationException">Thrown when API key is not configured.</exception>
    public static ILlmClient Create(
        TsugixConfig config, 
        ConfigManager configManager, 
        bool useSemanticKernel = false,
        RateLimiter? rateLimiter = null)
    {
        var apiKey = configManager.GetApiKey(config.Provider);
        
        if (string.IsNullOrEmpty(apiKey))
        {
            var envVarName = ConfigManager.GetApiKeyEnvVarName(config.Provider);
            throw new InvalidOperationException(
                $"No API key found for {config.Provider}. " +
                $"Set the {envVarName} environment variable.");
        }
        
        if (useSemanticKernel)
        {
            return new SemanticKernelLlmClient(config.Provider, config.ModelName, apiKey);
        }
        
        // Use provided rate limiter or default
        var limiter = rateLimiter ?? RateLimiter.Default;
        
        // Default: Use direct HTTP clients (AOT-friendly)
        return config.Provider switch
        {
            LlmProvider.OpenAI => new OpenAiHttpClient(apiKey, config.ModelName, config.Endpoint, limiter),
            LlmProvider.Anthropic => new AnthropicHttpClient(apiKey, config.ModelName, config.Endpoint, limiter),
            _ => throw new ArgumentOutOfRangeException(nameof(config.Provider), 
                $"Unsupported LLM provider: {config.Provider}")
        };
    }
    
    /// <summary>
    /// Creates LLM options from the configuration.
    /// </summary>
    /// <param name="config">The Tsugix configuration.</param>
    /// <returns>LLM options for API calls.</returns>
    public static LlmOptions CreateOptions(TsugixConfig config)
    {
        return new LlmOptions
        {
            MaxTokens = config.MaxTokens,
            Temperature = config.Temperature,
            TimeoutSeconds = config.TimeoutSeconds,
            RetryCount = config.RetryCount
        };
    }
}
