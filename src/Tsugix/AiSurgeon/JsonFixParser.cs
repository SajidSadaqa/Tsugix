using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tsugix.AiSurgeon;

/// <summary>
/// Parses JSON fix suggestions from LLM responses.
/// Handles both clean JSON and JSON embedded in markdown/text.
/// Validates against strict schema.
/// </summary>
public sealed partial class JsonFixParser : IFixParser
{
    /// <inheritdoc />
    public FixSuggestion? Parse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;
        
        // First, try to extract JSON if the response isn't pure JSON
        var json = ExtractJson(response) ?? response;
        
        try
        {
            var suggestion = JsonSerializer.Deserialize(json, TsugixJsonContext.Default.FixSuggestion);
            
            if (suggestion == null)
                return null;
            
            // Normalize to handle legacy format
            suggestion = suggestion.Normalize();
            
            // Validate against strict schema
            var validation = FixSuggestionValidator.Validate(suggestion);
            if (!validation.IsValid)
                return null;
            
            return suggestion;
        }
        catch (JsonException)
        {
            return null;
        }
    }
    
    /// <inheritdoc />
    public string? ExtractJson(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;
        
        // Try to find JSON in markdown code blocks first (```json ... ``` or ``` ... ```)
        var codeBlockMatch = MarkdownCodeBlockRegex().Match(response);
        if (codeBlockMatch.Success)
        {
            var extracted = codeBlockMatch.Groups[1].Value.Trim();
            if (IsValidJsonObject(extracted))
                return extracted;
        }
        
        // Try to find JSON object by balanced braces
        var braceIndex = response.IndexOf('{');
        if (braceIndex >= 0)
        {
            var potentialJson = ExtractBalancedJson(response, braceIndex);
            if (potentialJson != null && IsValidJsonObject(potentialJson))
                return potentialJson;
        }
        
        return null;
    }
    
    /// <summary>
    /// Validates that a string is valid JSON and represents an object.
    /// </summary>
    private static bool IsValidJsonObject(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Extracts a balanced JSON object from text starting at the given index.
    /// </summary>
    private static string? ExtractBalancedJson(string text, int startIndex)
    {
        var depth = 0;
        var inString = false;
        var escape = false;
        
        for (var i = startIndex; i < text.Length; i++)
        {
            var c = text[i];
            
            if (escape)
            {
                escape = false;
                continue;
            }
            
            if (c == '\\' && inString)
            {
                escape = true;
                continue;
            }
            
            if (c == '"')
            {
                inString = !inString;
                continue;
            }
            
            if (inString)
                continue;
            
            if (c == '{')
                depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return text.Substring(startIndex, i - startIndex + 1);
            }
        }
        
        return null;
    }
    
    [GeneratedRegex(@"```(?:json)?\s*\n?([\s\S]*?)\n?```", RegexOptions.Multiline)]
    private static partial Regex MarkdownCodeBlockRegex();
}
