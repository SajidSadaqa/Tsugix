namespace Tsugix.AiSurgeon;

/// <summary>
/// Supported LLM providers for AI-powered fix generation.
/// </summary>
public enum LlmProvider
{
    /// <summary>
    /// OpenAI (GPT-4, GPT-4o, etc.)
    /// </summary>
    OpenAI,
    
    /// <summary>
    /// Anthropic (Claude 3.5 Sonnet, Claude 3 Opus, etc.)
    /// </summary>
    Anthropic
}
