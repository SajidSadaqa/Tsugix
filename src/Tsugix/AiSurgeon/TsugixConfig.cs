using System.Text.Json.Serialization;

namespace Tsugix.AiSurgeon;

/// <summary>
/// Configuration settings for Tsugix AI Surgeon.
/// Loaded from .tsugix.json or environment variables.
/// </summary>
public sealed record TsugixConfig
{
    /// <summary>
    /// The LLM provider to use (OpenAI or Anthropic).
    /// </summary>
    [JsonPropertyName("provider")]
    public LlmProvider Provider { get; init; } = LlmProvider.OpenAI;
    
    /// <summary>
    /// The model name to use (e.g., "gpt-4o", "claude-3-5-sonnet").
    /// </summary>
    [JsonPropertyName("model")]
    public string ModelName { get; init; } = "gpt-4o";
    
    /// <summary>
    /// Maximum tokens for prompt (to stay within context limits).
    /// </summary>
    [JsonPropertyName("maxTokens")]
    public int MaxTokens { get; init; } = 8000;
    
    /// <summary>
    /// Whether to automatically create backups before patching.
    /// </summary>
    [JsonPropertyName("autoBackup")]
    public bool AutoBackup { get; init; } = true;
    
    /// <summary>
    /// Whether to automatically apply fixes without confirmation.
    /// </summary>
    [JsonPropertyName("autoApply")]
    public bool AutoApply { get; init; } = false;
    
    /// <summary>
    /// Whether to automatically re-run the command after applying fixes.
    /// </summary>
    [JsonPropertyName("autoRerun")]
    public bool AutoRerun { get; init; } = false;
    
    /// <summary>
    /// Timeout in seconds for LLM API calls.
    /// </summary>
    [JsonPropertyName("timeout")]
    public int TimeoutSeconds { get; init; } = 30;
    
    /// <summary>
    /// Number of retries for failed API calls.
    /// </summary>
    [JsonPropertyName("retryCount")]
    public int RetryCount { get; init; } = 1;
    
    /// <summary>
    /// Custom prompt template (null to use default).
    /// </summary>
    [JsonPropertyName("customPromptTemplate")]
    public string? CustomPromptTemplate { get; init; }
    
    /// <summary>
    /// Temperature for LLM responses (0.0-1.0, lower = more deterministic).
    /// </summary>
    [JsonPropertyName("temperature")]
    public double Temperature { get; init; } = 0.2;
    
    /// <summary>
    /// Custom API endpoint URL (null to use provider default).
    /// </summary>
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }
    
    /// <summary>
    /// Whether to use Semantic Kernel instead of direct HTTP (not AOT-friendly).
    /// </summary>
    [JsonPropertyName("useSemanticKernel")]
    public bool UseSemanticKernel { get; init; } = false;
    
    /// <summary>
    /// Root directory for file operations (null = working directory).
    /// Files outside this root cannot be patched unless --allow-outside-root is set.
    /// </summary>
    [JsonPropertyName("rootDirectory")]
    public string? RootDirectory { get; init; }
    
    /// <summary>
    /// Creates a default configuration.
    /// </summary>
    public static TsugixConfig Default => new();
}
