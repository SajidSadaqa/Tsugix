using Tsugix.ContextEngine.Parsers;

namespace Tsugix.ContextEngine;

/// <summary>
/// Registry for language-specific error parsers.
/// Selects the best parser based on confidence scoring.
/// </summary>
public class ParserRegistry
{
    private readonly List<IErrorParser> _parsers = new();
    
    /// <summary>
    /// Creates a ParserRegistry with all default language parsers registered.
    /// </summary>
    public static ParserRegistry CreateWithDefaultParsers()
    {
        var registry = new ParserRegistry();
        
        // Register all language parsers
        registry.Register(new PythonErrorParser());
        registry.Register(new NodeErrorParser());
        registry.Register(new CSharpErrorParser());
        registry.Register(new JavaErrorParser());
        registry.Register(new GoErrorParser());
        registry.Register(new RustErrorParser());
        registry.Register(new RubyErrorParser());
        registry.Register(new PhpErrorParser());
        registry.Register(new SwiftErrorParser());
        
        return registry;
    }
    
    /// <summary>
    /// Registers a parser with the registry.
    /// </summary>
    /// <param name="parser">The parser to register.</param>
    public void Register(IErrorParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        _parsers.Add(parser);
    }
    
    /// <summary>
    /// Gets the best parser for the given stderr content.
    /// </summary>
    /// <param name="stderr">The standard error output to analyze.</param>
    /// <returns>The parser with highest confidence, or null if none can parse.</returns>
    public IErrorParser? GetBestParser(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return null;
        
        IErrorParser? bestParser = null;
        var bestConfidence = ParserConfidence.None;
        
        foreach (var parser in _parsers)
        {
            var confidence = parser.CanParse(stderr);
            if (confidence > bestConfidence)
            {
                bestConfidence = confidence;
                bestParser = parser;
            }
        }
        
        return bestConfidence > ParserConfidence.None ? bestParser : null;
    }
    
    /// <summary>
    /// Gets all registered parsers.
    /// </summary>
    public IReadOnlyList<IErrorParser> GetAllParsers() => _parsers.AsReadOnly();
    
    /// <summary>
    /// Gets the number of registered parsers.
    /// </summary>
    public int Count => _parsers.Count;
}
