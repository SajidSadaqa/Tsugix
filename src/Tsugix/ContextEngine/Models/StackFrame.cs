namespace Tsugix.ContextEngine;

/// <summary>
/// A single frame in a stack trace.
/// </summary>
public sealed record StackFrame
{
    /// <summary>
    /// Path to the source file (may be null for native/library code).
    /// </summary>
    public string? FilePath { get; init; }
    
    /// <summary>
    /// The line number where the error occurred (1-based).
    /// </summary>
    public int? LineNumber { get; init; }
    
    /// <summary>
    /// The column number where the error occurred (1-based, if available).
    /// </summary>
    public int? ColumnNumber { get; init; }
    
    /// <summary>
    /// The function/method name.
    /// </summary>
    public string? FunctionName { get; init; }
    
    /// <summary>
    /// The class/module name (if applicable).
    /// </summary>
    public string? ClassName { get; init; }
    
    /// <summary>
    /// Whether this frame is user code (vs library/framework code).
    /// </summary>
    public bool IsUserCode { get; init; } = true;
    
    /// <summary>
    /// Source code context around this frame (populated by SourceReader).
    /// </summary>
    public SourceSnippet? SourceContext { get; init; }
}
