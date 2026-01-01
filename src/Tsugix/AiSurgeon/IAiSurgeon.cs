using Tsugix.ContextEngine;

namespace Tsugix.AiSurgeon;

/// <summary>
/// Options for AI Surgeon analysis.
/// </summary>
public sealed record AiSurgeonOptions
{
    /// <summary>
    /// Whether to skip AI analysis entirely.
    /// </summary>
    public bool SkipAi { get; init; } = false;
    
    /// <summary>
    /// Whether to automatically apply fixes without confirmation.
    /// </summary>
    public bool AutoApply { get; init; } = false;
    
    /// <summary>
    /// Whether to automatically re-run the command after applying fixes.
    /// </summary>
    public bool AutoRerun { get; init; } = false;
    
    /// <summary>
    /// Whether to skip the re-run prompt.
    /// </summary>
    public bool NoRerun { get; init; } = false;
    
    /// <summary>
    /// Whether to allow patching files outside the root directory.
    /// Security risk - use with caution.
    /// </summary>
    public bool AllowOutsideRoot { get; init; } = false;
    
    /// <summary>
    /// Whether to enable verbose logging output.
    /// </summary>
    public bool Verbose { get; init; } = false;
}

/// <summary>
/// Orchestrates AI-powered fix generation and application.
/// </summary>
public interface IAiSurgeon
{
    /// <summary>
    /// Analyzes an error and suggests/applies fixes.
    /// </summary>
    /// <param name="errorContext">The error context from Phase 2.</param>
    /// <param name="options">Options for the analysis.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the fix attempt.</returns>
    Task<FixResult> AnalyzeAndFixAsync(
        ErrorContext errorContext,
        AiSurgeonOptions options,
        CancellationToken cancellationToken = default);
}
