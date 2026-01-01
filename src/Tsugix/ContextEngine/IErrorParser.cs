namespace Tsugix.ContextEngine;

/// <summary>
/// Strategy interface for language-specific error parsing.
/// Each language (Python, Node.js, C#, etc.) implements this interface.
/// </summary>
public interface IErrorParser
{
    /// <summary>
    /// The name of the language this parser handles.
    /// </summary>
    string LanguageName { get; }
    
    /// <summary>
    /// Determines if this parser can handle the given stderr content.
    /// </summary>
    /// <param name="stderr">The standard error output to analyze.</param>
    /// <returns>Confidence level indicating how well this parser can handle the input.</returns>
    ParserConfidence CanParse(string stderr);
    
    /// <summary>
    /// Parses the stderr content into structured error information.
    /// </summary>
    /// <param name="stderr">The standard error output to parse.</param>
    /// <returns>Parsed result containing stack frames and exception info.</returns>
    ParseResult Parse(string stderr);
}
