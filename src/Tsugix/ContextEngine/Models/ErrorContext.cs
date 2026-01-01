namespace Tsugix.ContextEngine;

/// <summary>
/// Complete structured error context for AI analysis.
/// This is the output of Phase 2 and input to Phase 3.
/// </summary>
public sealed record ErrorContext
{
    /// <summary>
    /// The detected programming language.
    /// </summary>
    public required string Language { get; init; }
    
    /// <summary>
    /// Information about the exception/error.
    /// </summary>
    public required ExceptionInfo Exception { get; init; }
    
    /// <summary>
    /// All parsed stack frames with source context.
    /// </summary>
    public required IReadOnlyList<StackFrame> Frames { get; init; }
    
    /// <summary>
    /// The primary frame (typically the first user code frame).
    /// </summary>
    public StackFrame? PrimaryFrame { get; init; }
    
    /// <summary>
    /// The original command that was executed.
    /// </summary>
    public required string OriginalCommand { get; init; }
    
    /// <summary>
    /// Timestamp when the error occurred.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }
    
    /// <summary>
    /// The working directory where the command was executed.
    /// </summary>
    public required string WorkingDirectory { get; init; }
}
