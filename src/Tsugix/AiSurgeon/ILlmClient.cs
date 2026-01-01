namespace Tsugix.AiSurgeon;

/// <summary>
/// Options for LLM completion requests.
/// </summary>
public sealed record LlmOptions
{
    /// <summary>
    /// Maximum tokens for the response.
    /// </summary>
    public int MaxTokens { get; init; } = 4000;
    
    /// <summary>
    /// Temperature for response generation (0.0-1.0).
    /// </summary>
    public double Temperature { get; init; } = 0.2;
    
    /// <summary>
    /// Timeout in seconds for the API call.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;
    
    /// <summary>
    /// Number of retries on failure.
    /// </summary>
    public int RetryCount { get; init; } = 1;
}

/// <summary>
/// Abstraction for LLM communication.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Sends a prompt and receives a response.
    /// </summary>
    /// <param name="systemPrompt">The system prompt defining AI behavior.</param>
    /// <param name="userPrompt">The user prompt with the actual request.</param>
    /// <param name="options">Options for the completion request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The LLM response text.</returns>
    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        LlmOptions options,
        CancellationToken cancellationToken = default);
}
