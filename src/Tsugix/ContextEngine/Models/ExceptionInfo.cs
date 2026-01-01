namespace Tsugix.ContextEngine;

/// <summary>
/// Information about the exception/error that was thrown.
/// </summary>
public sealed record ExceptionInfo
{
    /// <summary>
    /// The type/class of the exception (e.g., "ValueError", "NullReferenceException").
    /// </summary>
    public required string Type { get; init; }
    
    /// <summary>
    /// The error message.
    /// </summary>
    public required string Message { get; init; }
    
    /// <summary>
    /// Inner exception information, if any.
    /// </summary>
    public string? InnerException { get; init; }
}
