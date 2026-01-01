namespace Tsugix.ContextEngine;

/// <summary>
/// Reads source code context from files.
/// </summary>
public interface ISourceReader
{
    /// <summary>
    /// Reads a window of lines around the specified line number.
    /// </summary>
    /// <param name="filePath">Path to the source file.</param>
    /// <param name="lineNumber">The line number to center the window on.</param>
    /// <param name="windowSize">Number of lines before and after the error line (default: 10).</param>
    /// <returns>Source snippet containing the context, or null if file cannot be read.</returns>
    SourceSnippet? ReadContext(string filePath, int lineNumber, int windowSize = 10);
}
