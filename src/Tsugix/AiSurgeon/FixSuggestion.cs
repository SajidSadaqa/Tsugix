using System.Text.Json.Serialization;

namespace Tsugix.AiSurgeon;

/// <summary>
/// A structured fix suggestion from the AI.
/// Strict schema with required fields for validation.
/// </summary>
public sealed record FixSuggestion
{
    /// <summary>
    /// The programming language.
    /// </summary>
    [JsonPropertyName("language")]
    public string? Language { get; init; }
    
    /// <summary>
    /// Array of edits to apply.
    /// </summary>
    [JsonPropertyName("edits")]
    public IReadOnlyList<FixEdit>? Edits { get; init; }
    
    /// <summary>
    /// Human-readable explanation of the fix (max 100 chars).
    /// </summary>
    [JsonPropertyName("explanation")]
    public string? Explanation { get; init; }
    
    /// <summary>
    /// Confidence score (0-100).
    /// </summary>
    [JsonPropertyName("confidence")]
    public int Confidence { get; init; }
    
    // Legacy fields for backward compatibility with old format
    
    /// <summary>
    /// Path to the file to modify (legacy format).
    /// </summary>
    [JsonPropertyName("filePath")]
    public string? FilePath { get; init; }
    
    /// <summary>
    /// The original lines to replace (legacy format).
    /// </summary>
    [JsonPropertyName("originalLines")]
    public IReadOnlyList<string>? OriginalLines { get; init; }
    
    /// <summary>
    /// The replacement lines to insert (legacy format).
    /// </summary>
    [JsonPropertyName("replacementLines")]
    public IReadOnlyList<string>? ReplacementLines { get; init; }
    
    /// <summary>
    /// Optional start line number (legacy format).
    /// </summary>
    [JsonPropertyName("startLine")]
    public int? StartLine { get; init; }
    
    /// <summary>
    /// Optional end line number (legacy format).
    /// </summary>
    [JsonPropertyName("endLine")]
    public int? EndLine { get; init; }
    
    /// <summary>
    /// Converts legacy format to new format with edits array.
    /// </summary>
    public FixSuggestion Normalize()
    {
        // If already has edits, return as-is
        if (Edits != null && Edits.Count > 0)
            return this;
        
        // Convert legacy format to new format
        if (!string.IsNullOrEmpty(FilePath) && OriginalLines != null)
        {
            var edit = new FixEdit
            {
                FilePath = FilePath,
                StartLine = StartLine ?? 0,
                EndLine = EndLine ?? (StartLine ?? 0) + OriginalLines.Count - 1,
                OriginalLines = OriginalLines,
                Replacement = ReplacementLines != null 
                    ? string.Join("\n", ReplacementLines) 
                    : string.Empty
            };
            
            return this with { Edits = new[] { edit } };
        }
        
        return this;
    }
}

/// <summary>
/// A single edit within a fix suggestion.
/// </summary>
public sealed record FixEdit
{
    /// <summary>
    /// Path to the file to modify.
    /// </summary>
    [JsonPropertyName("filePath")]
    public required string FilePath { get; init; }
    
    /// <summary>
    /// 1-based start line number (inclusive).
    /// </summary>
    [JsonPropertyName("startLine")]
    public int StartLine { get; init; }
    
    /// <summary>
    /// 1-based end line number (inclusive).
    /// </summary>
    [JsonPropertyName("endLine")]
    public int EndLine { get; init; }
    
    /// <summary>
    /// The original lines to replace (array of strings, one per line).
    /// </summary>
    [JsonPropertyName("originalLines")]
    public IReadOnlyList<string>? OriginalLines { get; init; }
    
    /// <summary>
    /// The replacement text (single string with \n for newlines).
    /// </summary>
    [JsonPropertyName("replacement")]
    public string? Replacement { get; init; }
}

/// <summary>
/// Validation result for a fix suggestion.
/// </summary>
public sealed record FixValidationResult
{
    /// <summary>
    /// Whether the fix suggestion is valid.
    /// </summary>
    public bool IsValid { get; init; }
    
    /// <summary>
    /// Validation error messages.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Creates a valid result.
    /// </summary>
    public static FixValidationResult Valid() => new() { IsValid = true };
    
    /// <summary>
    /// Creates an invalid result with errors.
    /// </summary>
    public static FixValidationResult Invalid(params string[] errors) => new()
    {
        IsValid = false,
        Errors = errors
    };
}

/// <summary>
/// Validator for fix suggestions.
/// </summary>
public static class FixSuggestionValidator
{
    /// <summary>
    /// Validates a fix suggestion against the strict schema.
    /// </summary>
    public static FixValidationResult Validate(FixSuggestion? suggestion)
    {
        if (suggestion == null)
            return FixValidationResult.Invalid("Fix suggestion is null");
        
        var errors = new List<string>();
        
        // Normalize to handle legacy format
        var normalized = suggestion.Normalize();
        
        // Validate edits
        if (normalized.Edits == null || normalized.Edits.Count == 0)
        {
            errors.Add("No edits provided");
        }
        else
        {
            for (var i = 0; i < normalized.Edits.Count; i++)
            {
                var edit = normalized.Edits[i];
                var prefix = normalized.Edits.Count > 1 ? $"Edit {i + 1}: " : "";
                
                if (string.IsNullOrWhiteSpace(edit.FilePath))
                    errors.Add($"{prefix}filePath is required");
                
                if (edit.StartLine <= 0)
                    errors.Add($"{prefix}startLine must be positive");
                
                if (edit.EndLine < edit.StartLine)
                    errors.Add($"{prefix}endLine must be >= startLine");
                
                if (edit.OriginalLines == null || edit.OriginalLines.Count == 0)
                    errors.Add($"{prefix}originalLines is required");
                
                if (edit.Replacement == null)
                    errors.Add($"{prefix}replacement is required");
            }
            
            // Check for overlapping edits in the same file
            var editsByFile = normalized.Edits.GroupBy(e => e.FilePath);
            foreach (var group in editsByFile)
            {
                var sortedEdits = group.OrderBy(e => e.StartLine).ToList();
                for (var i = 0; i < sortedEdits.Count - 1; i++)
                {
                    if (sortedEdits[i].EndLine >= sortedEdits[i + 1].StartLine)
                    {
                        errors.Add($"Overlapping edits in {group.Key}: lines {sortedEdits[i].StartLine}-{sortedEdits[i].EndLine} and {sortedEdits[i + 1].StartLine}-{sortedEdits[i + 1].EndLine}");
                    }
                }
            }
        }
        
        // Validate confidence
        if (normalized.Confidence < 0 || normalized.Confidence > 100)
            errors.Add("confidence must be between 0 and 100");
        
        // Validate explanation (optional but if present, should be short)
        if (normalized.Explanation != null && normalized.Explanation.Length > 200)
            errors.Add("explanation should be max 200 characters");
        
        return errors.Count == 0 
            ? FixValidationResult.Valid() 
            : FixValidationResult.Invalid(errors.ToArray());
    }
}
