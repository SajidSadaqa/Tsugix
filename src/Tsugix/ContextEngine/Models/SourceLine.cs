namespace Tsugix.ContextEngine;

/// <summary>
/// A single line of source code.
/// </summary>
public sealed record SourceLine
{
    /// <summary>
    /// The 1-based line number.
    /// </summary>
    public required int LineNumber { get; init; }
    
    /// <summary>
    /// The content of the line.
    /// </summary>
    public required string Content { get; init; }
    
    /// <summary>
    /// Whether this is the line where the error occurred.
    /// </summary>
    public bool IsErrorLine { get; init; }
}
