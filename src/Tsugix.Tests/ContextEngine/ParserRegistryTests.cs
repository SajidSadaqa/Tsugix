using FsCheck;
using FsCheck.Xunit;
using Tsugix.ContextEngine;
using Xunit;

namespace Tsugix.Tests.ContextEngine;

/// <summary>
/// Property-based tests for ParserRegistry.
/// Feature: tsugix-phase2-context-engine, Property 13: Confidence-Based Parser Selection
/// Validates: Requirements 13.4
/// </summary>
public class ParserRegistryTests
{
    /// <summary>
    /// Mock parser for testing.
    /// </summary>
    private class MockParser : IErrorParser
    {
        public string LanguageName { get; }
        public ParserConfidence Confidence { get; }
        
        public MockParser(string name, ParserConfidence confidence)
        {
            LanguageName = name;
            Confidence = confidence;
        }
        
        public ParserConfidence CanParse(string stderr) => Confidence;
        
        public ParseResult Parse(string stderr) => ParseResult.Failed();
    }
    
    /// <summary>
    /// Property 13: Confidence-Based Parser Selection
    /// For any stderr content where multiple parsers return non-zero confidence,
    /// the ParserRegistry SHALL select the parser with the highest confidence score.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property HighestConfidenceParserIsSelected()
    {
        return Prop.ForAll(
            Arb.From<NonEmptyString>(),
            (input) =>
            {
                var registry = new ParserRegistry();
                
                // Register parsers with different confidence levels
                var lowParser = new MockParser("Low", ParserConfidence.Low);
                var mediumParser = new MockParser("Medium", ParserConfidence.Medium);
                var highParser = new MockParser("High", ParserConfidence.High);
                
                // Register in random order
                registry.Register(mediumParser);
                registry.Register(lowParser);
                registry.Register(highParser);
                
                var selected = registry.GetBestParser(input.Get);
                
                // Should always select the highest confidence parser
                return selected?.LanguageName == "High";
            });
    }
    
    /// <summary>
    /// Property: Empty input returns null.
    /// </summary>
    [Fact]
    public void EmptyInput_ReturnsNull()
    {
        var registry = new ParserRegistry();
        registry.Register(new MockParser("Test", ParserConfidence.High));
        
        Assert.Null(registry.GetBestParser(""));
        Assert.Null(registry.GetBestParser(null!));
    }
    
    /// <summary>
    /// Property: No parsers returns null.
    /// </summary>
    [Fact]
    public void NoParsers_ReturnsNull()
    {
        var registry = new ParserRegistry();
        Assert.Null(registry.GetBestParser("some error"));
    }
    
    /// <summary>
    /// Property: All None confidence returns null.
    /// </summary>
    [Fact]
    public void AllNoneConfidence_ReturnsNull()
    {
        var registry = new ParserRegistry();
        registry.Register(new MockParser("A", ParserConfidence.None));
        registry.Register(new MockParser("B", ParserConfidence.None));
        
        Assert.Null(registry.GetBestParser("some error"));
    }
    
    /// <summary>
    /// Property: First parser with highest confidence wins (tie-breaker).
    /// </summary>
    [Fact]
    public void TieBreaker_FirstRegisteredWins()
    {
        var registry = new ParserRegistry();
        var first = new MockParser("First", ParserConfidence.High);
        var second = new MockParser("Second", ParserConfidence.High);
        
        registry.Register(first);
        registry.Register(second);
        
        var selected = registry.GetBestParser("error");
        
        // First registered with same confidence wins
        Assert.Equal("First", selected?.LanguageName);
    }
    
    /// <summary>
    /// Property: Parser count is correct.
    /// </summary>
    [Fact]
    public void ParserCount_IsCorrect()
    {
        var registry = new ParserRegistry();
        Assert.Equal(0, registry.Count);
        
        registry.Register(new MockParser("A", ParserConfidence.Low));
        Assert.Equal(1, registry.Count);
        
        registry.Register(new MockParser("B", ParserConfidence.Medium));
        Assert.Equal(2, registry.Count);
    }
}
