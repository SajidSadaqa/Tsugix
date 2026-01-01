namespace Tsugix.AiSurgeon;

/// <summary>
/// The outcome of a fix attempt.
/// </summary>
public enum FixOutcome
{
    /// <summary>
    /// Fix was successfully applied.
    /// </summary>
    Applied,
    
    /// <summary>
    /// User rejected the fix.
    /// </summary>
    Rejected,
    
    /// <summary>
    /// Fix application failed (file error, content mismatch, etc.).
    /// </summary>
    Failed,
    
    /// <summary>
    /// AI did not suggest a fix.
    /// </summary>
    NoFixSuggested,
    
    /// <summary>
    /// AI service error (timeout, rate limit, etc.).
    /// </summary>
    AiError,
    
    /// <summary>
    /// Fix was skipped (--skip-ai flag or user choice).
    /// </summary>
    Skipped
}

/// <summary>
/// Result of a fix attempt.
/// </summary>
public sealed record FixResult
{
    /// <summary>
    /// The outcome of the fix attempt.
    /// </summary>
    public required FixOutcome Outcome { get; init; }
    
    /// <summary>
    /// The fix suggestion (if any).
    /// </summary>
    public FixSuggestion? Suggestion { get; init; }
    
    /// <summary>
    /// Path to the backup file (if created).
    /// </summary>
    public string? BackupPath { get; init; }
    
    /// <summary>
    /// Error message (if failed).
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static FixResult Applied(FixSuggestion suggestion, string backupPath) => new()
    {
        Outcome = FixOutcome.Applied,
        Suggestion = suggestion,
        BackupPath = backupPath
    };
    
    /// <summary>
    /// Creates a rejected result.
    /// </summary>
    public static FixResult Rejected(FixSuggestion suggestion) => new()
    {
        Outcome = FixOutcome.Rejected,
        Suggestion = suggestion
    };
    
    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static FixResult Failed(string errorMessage, FixSuggestion? suggestion = null) => new()
    {
        Outcome = FixOutcome.Failed,
        Suggestion = suggestion,
        ErrorMessage = errorMessage
    };
    
    /// <summary>
    /// Creates a no-fix result.
    /// </summary>
    public static FixResult NoFix() => new()
    {
        Outcome = FixOutcome.NoFixSuggested
    };
    
    /// <summary>
    /// Creates an AI error result.
    /// </summary>
    public static FixResult AiError(string errorMessage) => new()
    {
        Outcome = FixOutcome.AiError,
        ErrorMessage = errorMessage
    };
    
    /// <summary>
    /// Creates a skipped result.
    /// </summary>
    public static FixResult Skipped() => new()
    {
        Outcome = FixOutcome.Skipped
    };
}
