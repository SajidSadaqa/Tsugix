namespace Tsugix.ContextEngine;

/// <summary>
/// Result of parsing an error/stack trace.
/// </summary>
public sealed record ParseResult
{
    /// <summary>
    /// Whether parsing was successful.
    /// </summary>
    public required bool Success { get; init; }
    
    /// <summary>
    /// Information about the exception/error.
    /// </summary>
    public ExceptionInfo? Exception { get; init; }
    
    /// <summary>
    /// The parsed stack frames.
    /// </summary>
    public required IReadOnlyList<StackFrame> Frames { get; init; }
    
    /// <summary>
    /// The raw error text (for debugging/fallback).
    /// </summary>
    public string? RawError { get; init; }
    
    /// <summary>
    /// Creates a failed parse result.
    /// </summary>
    public static ParseResult Failed(string? rawError = null) => new()
    {
        Success = false,
        Frames = Array.Empty<StackFrame>(),
        RawError = rawError
    };
    
    /// <summary>
    /// Creates a successful parse result.
    /// </summary>
    public static ParseResult Succeeded(ExceptionInfo exception, IReadOnlyList<StackFrame> frames) => new()
    {
        Success = true,
        Exception = exception,
        Frames = frames
    };
}
