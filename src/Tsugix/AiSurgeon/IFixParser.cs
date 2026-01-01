namespace Tsugix.AiSurgeon;

/// <summary>
/// Parses LLM responses into structured fix suggestions.
/// </summary>
public interface IFixParser
{
    /// <summary>
    /// Parses a JSON response into a fix suggestion.
    /// </summary>
    /// <param name="response">The raw LLM response.</param>
    /// <returns>The parsed fix suggestion, or null if parsing failed.</returns>
    FixSuggestion? Parse(string response);
    
    /// <summary>
    /// Attempts to extract JSON from a potentially malformed response.
    /// </summary>
    /// <param name="response">The raw LLM response.</param>
    /// <returns>The extracted JSON string, or null if no JSON found.</returns>
    string? ExtractJson(string response);
}
