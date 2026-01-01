using Tsugix.ContextEngine;

namespace Tsugix.AiSurgeon;

/// <summary>
/// Options for prompt building.
/// </summary>
public sealed record PromptOptions
{
    /// <summary>
    /// Maximum tokens for the prompt (to stay within context limits).
    /// </summary>
    public int MaxTokens { get; init; } = 8000;
    
    /// <summary>
    /// Custom prompt template (null to use default).
    /// </summary>
    public string? CustomPromptTemplate { get; init; }
    
    /// <summary>
    /// Number of context lines to include around the error.
    /// </summary>
    public int ContextLines { get; init; } = 10;
}

/// <summary>
/// Builds prompts from error context for LLM fix generation.
/// </summary>
public interface IPromptBuilder
{
    /// <summary>
    /// Builds the system prompt defining AI behavior.
    /// </summary>
    string BuildSystemPrompt();
    
    /// <summary>
    /// Builds the user prompt with error context.
    /// </summary>
    /// <param name="errorContext">The error context from Phase 2.</param>
    /// <param name="options">Options for prompt building.</param>
    /// <returns>The formatted user prompt.</returns>
    string BuildUserPrompt(ErrorContext errorContext, PromptOptions options);
}
