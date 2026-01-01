namespace Tsugix.ContextEngine;

/// <summary>
/// A window of source code lines around an error location.
/// </summary>
public sealed record SourceSnippet
{
    /// <summary>
    /// Path to the source file.
    /// </summary>
    public required string FilePath { get; init; }
    
    /// <summary>
    /// The first line number in the snippet (1-based).
    /// </summary>
    public required int StartLine { get; init; }
    
    /// <summary>
    /// The last line number in the snippet (1-based).
    /// </summary>
    public required int EndLine { get; init; }
    
    /// <summary>
    /// The line number where the error occurred (1-based).
    /// </summary>
    public required int ErrorLine { get; init; }
    
    /// <summary>
    /// The source code lines in the snippet.
    /// </summary>
    public required IReadOnlyList<SourceLine> Lines { get; init; }
}
