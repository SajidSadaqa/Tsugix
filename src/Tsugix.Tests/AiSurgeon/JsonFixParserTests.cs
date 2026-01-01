using FsCheck;
using FsCheck.Xunit;
using System.Text.Json;
using Tsugix.AiSurgeon;
using Xunit;

namespace Tsugix.Tests.AiSurgeon;

/// <summary>
/// Tests for JsonFixParser.
/// Validates: Requirements 3.2, 3.3, 3.4, 3.5, 3.6, 3.7
/// </summary>
public class JsonFixParserTests
{
    private readonly JsonFixParser _parser = new();
    
    [Fact]
    public void Parse_ValidNewFormat_ReturnsFixSuggestion()
    {
        var json = """
            {
                "language": "Python",
                "edits": [
                    {
                        "filePath": "test.py",
                        "startLine": 10,
                        "endLine": 12,
                        "originalLines": ["    x = 1", "    y = 2", "    z = x / y"],
                        "replacement": "    x = 1\n    y = 2\n    z = x / y if y != 0 else 0"
                    }
                ],
                "explanation": "Added zero check",
                "confidence": 85
            }
            """;
        
        var result = _parser.Parse(json);
        
        Assert.NotNull(result);
        Assert.Equal("Python", result.Language);
        Assert.Single(result.Edits!);
        Assert.Equal("test.py", result.Edits![0].FilePath);
        Assert.Equal(10, result.Edits[0].StartLine);
        Assert.Equal(12, result.Edits[0].EndLine);
        Assert.Equal(3, result.Edits[0].OriginalLines!.Count);
        Assert.Equal(85, result.Confidence);
    }
    
    [Fact]
    public void Parse_ValidLegacyFormat_ReturnsNormalizedFixSuggestion()
    {
        var json = """
            {
                "filePath": "test.py",
                "originalLines": ["x = 1"],
                "replacementLines": ["x = 10"],
                "explanation": "Fixed the value",
                "confidence": 90,
                "startLine": 5
            }
            """;
        
        var result = _parser.Parse(json);
        
        Assert.NotNull(result);
        Assert.Single(result.Edits!);
        Assert.Equal("test.py", result.Edits![0].FilePath);
        Assert.Equal(5, result.Edits[0].StartLine);
        Assert.Equal("x = 10", result.Edits[0].Replacement);
    }
    
    [Fact]
    public void Parse_InvalidJson_ReturnsNull()
    {
        var result = _parser.Parse("not valid json");
        
        Assert.Null(result);
    }
    
    [Fact]
    public void Parse_MissingRequiredFields_ReturnsNull()
    {
        var json = """
            {
                "explanation": "Missing edits",
                "confidence": 50
            }
            """;
        
        var result = _parser.Parse(json);
        
        Assert.Null(result);
    }
    
    [Fact]
    public void Parse_InvalidConfidence_ReturnsNull()
    {
        var json = """
            {
                "filePath": "test.py",
                "originalLines": ["x = 1"],
                "replacementLines": ["x = 10"],
                "explanation": "Fix",
                "confidence": 150
            }
            """;
        
        var result = _parser.Parse(json);
        
        Assert.Null(result);
    }
    
    [Fact]
    public void Parse_NegativeConfidence_ReturnsNull()
    {
        var json = """
            {
                "filePath": "test.py",
                "originalLines": ["x = 1"],
                "replacementLines": ["x = 10"],
                "explanation": "Fix",
                "confidence": -10
            }
            """;
        
        var result = _parser.Parse(json);
        
        Assert.Null(result);
    }
    
    [Fact]
    public void ExtractJson_FromMarkdownCodeBlock_ReturnsJson()
    {
        var response = """
            Here's the fix:
            
            ```json
            {
                "filePath": "test.py",
                "originalLines": ["x = 1"],
                "replacementLines": ["x = 10"],
                "explanation": "Fix",
                "confidence": 80
            }
            ```
            
            This should work!
            """;
        
        var json = _parser.ExtractJson(response);
        
        Assert.NotNull(json);
        Assert.Contains("filePath", json);
    }
    
    [Fact]
    public void ExtractJson_FromPlainCodeBlock_ReturnsJson()
    {
        var response = """
            ```
            {"filePath": "test.py", "originalLines": ["x"], "replacementLines": ["y"], "explanation": "fix", "confidence": 50}
            ```
            """;
        
        var json = _parser.ExtractJson(response);
        
        Assert.NotNull(json);
    }
    
    [Fact]
    public void ExtractJson_FromSurroundingText_ReturnsJson()
    {
        var response = """
            I found the issue. Here's my suggestion:
            {"filePath": "test.py", "originalLines": ["x"], "replacementLines": ["y"], "explanation": "fix", "confidence": 50}
            Let me know if you need anything else.
            """;
        
        var json = _parser.ExtractJson(response);
        
        Assert.NotNull(json);
        Assert.StartsWith("{", json);
        Assert.EndsWith("}", json);
    }
    
    [Fact]
    public void ExtractJson_NoJson_ReturnsNull()
    {
        var response = "This response has no JSON at all.";
        
        var json = _parser.ExtractJson(response);
        
        Assert.Null(json);
    }
    
    [Fact]
    public void Parse_OverlappingEdits_ReturnsNull()
    {
        var json = """
            {
                "language": "Python",
                "edits": [
                    {
                        "filePath": "test.py",
                        "startLine": 10,
                        "endLine": 15,
                        "originalLines": ["line1"],
                        "replacement": "fixed1"
                    },
                    {
                        "filePath": "test.py",
                        "startLine": 12,
                        "endLine": 18,
                        "originalLines": ["line2"],
                        "replacement": "fixed2"
                    }
                ],
                "explanation": "Overlapping edits",
                "confidence": 80
            }
            """;
        
        var result = _parser.Parse(json);
        
        Assert.Null(result);
    }
    
    [Fact]
    public void Parse_MultipleNonOverlappingEdits_ReturnsFixSuggestion()
    {
        var json = """
            {
                "language": "Python",
                "edits": [
                    {
                        "filePath": "test.py",
                        "startLine": 10,
                        "endLine": 12,
                        "originalLines": ["line1"],
                        "replacement": "fixed1"
                    },
                    {
                        "filePath": "test.py",
                        "startLine": 20,
                        "endLine": 22,
                        "originalLines": ["line2"],
                        "replacement": "fixed2"
                    }
                ],
                "explanation": "Multiple fixes",
                "confidence": 80
            }
            """;
        
        var result = _parser.Parse(json);
        
        Assert.NotNull(result);
        Assert.Equal(2, result.Edits!.Count);
    }
}

/// <summary>
/// Property-based tests for JsonFixParser.
/// Validates: Requirements 3.2, 3.3, 3.4, 3.5, 3.6, 3.7
/// </summary>
public class JsonFixParserPropertyTests
{
    private readonly JsonFixParser _parser = new();
    
    /// <summary>
    /// Property 3: FixSuggestion Parsing Round-Trip
    /// For any valid FixSuggestion, serializing to JSON and parsing back
    /// SHALL produce an equivalent FixSuggestion.
    /// Validates: Requirements 3.2, 3.3, 3.4, 3.5, 3.6
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FixSuggestionRoundTrip()
    {
        var filePathGen = Gen.Elements("test.py", "app.js", "main.cs", "lib.go");
        var lineGen = Gen.Choose(1, 1000);
        var confidenceGen = Gen.Choose(0, 100);
        var codeLineGen = Gen.Elements("x = 1", "return value", "if condition:", "    pass");
        
        var editGen = from filePath in filePathGen
                      from startLine in lineGen
                      from lineCount in Gen.Choose(1, 5)
                      from originalLines in Gen.ListOf(lineCount, codeLineGen)
                      from replacement in codeLineGen
                      select new FixEdit
                      {
                          FilePath = filePath,
                          StartLine = startLine,
                          EndLine = startLine + lineCount - 1,
                          OriginalLines = originalLines.ToList(),
                          Replacement = replacement
                      };
        
        var suggestionGen = from edit in editGen
                            from confidence in confidenceGen
                            select new FixSuggestion
                            {
                                Language = "Python",
                                Edits = new[] { edit },
                                Explanation = "Test fix",
                                Confidence = confidence
                            };
        
        return Prop.ForAll(
            suggestionGen.ToArbitrary(),
            suggestion =>
            {
                var json = JsonSerializer.Serialize(suggestion, TsugixJsonContext.Default.FixSuggestion);
                var parsed = _parser.Parse(json);
                
                if (parsed == null)
                    return false;
                
                return parsed.Confidence == suggestion.Confidence &&
                       parsed.Edits!.Count == suggestion.Edits!.Count &&
                       parsed.Edits[0].FilePath == suggestion.Edits[0].FilePath &&
                       parsed.Edits[0].StartLine == suggestion.Edits[0].StartLine;
            });
    }
    
    /// <summary>
    /// Property 4: JSON Extraction from Malformed Responses
    /// For any valid JSON embedded in markdown or surrounding text,
    /// the parser SHALL extract and validate the JSON correctly.
    /// Validates: Requirements 3.7
    /// </summary>
    [Property(MaxTest = 50)]
    public Property JsonExtractionFromMalformedResponses()
    {
        var prefixGen = Gen.Elements(
            "Here's the fix:\n",
            "I found the issue:\n\n",
            "```json\n",
            "```\n",
            "");
        
        var suffixGen = Gen.Elements(
            "\n```",
            "\n\nLet me know!",
            "",
            "\n```\nDone.");
        
        var validJsonGen = Gen.Constant("""
            {"filePath":"test.py","originalLines":["x"],"replacementLines":["y"],"explanation":"fix","confidence":50,"startLine":1}
            """);
        
        var responseGen = from prefix in prefixGen
                          from json in validJsonGen
                          from suffix in suffixGen
                          select prefix + json + suffix;
        
        return Prop.ForAll(
            responseGen.ToArbitrary(),
            response =>
            {
                var extracted = _parser.ExtractJson(response);
                
                if (extracted == null)
                    return false;
                
                // Should be valid JSON
                try
                {
                    using var doc = JsonDocument.Parse(extracted);
                    return doc.RootElement.ValueKind == JsonValueKind.Object;
                }
                catch
                {
                    return false;
                }
            });
    }
}

/// <summary>
/// Tests for FixSuggestionValidator.
/// </summary>
public class FixSuggestionValidatorTests
{
    [Fact]
    public void Validate_ValidSuggestion_ReturnsValid()
    {
        var suggestion = new FixSuggestion
        {
            Language = "Python",
            Edits = new[]
            {
                new FixEdit
                {
                    FilePath = "test.py",
                    StartLine = 10,
                    EndLine = 12,
                    OriginalLines = new[] { "x = 1" },
                    Replacement = "x = 2"
                }
            },
            Explanation = "Fixed",
            Confidence = 80
        };
        
        var result = FixSuggestionValidator.Validate(suggestion);
        
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
    
    [Fact]
    public void Validate_NullSuggestion_ReturnsInvalid()
    {
        var result = FixSuggestionValidator.Validate(null);
        
        Assert.False(result.IsValid);
        Assert.Contains("null", result.Errors[0]);
    }
    
    [Fact]
    public void Validate_EmptyFilePath_ReturnsInvalid()
    {
        var suggestion = new FixSuggestion
        {
            Edits = new[]
            {
                new FixEdit
                {
                    FilePath = "",
                    StartLine = 1,
                    EndLine = 1,
                    OriginalLines = new[] { "x" },
                    Replacement = "y"
                }
            },
            Confidence = 50
        };
        
        var result = FixSuggestionValidator.Validate(suggestion);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("filePath"));
    }
    
    [Fact]
    public void Validate_InvalidLineRange_ReturnsInvalid()
    {
        var suggestion = new FixSuggestion
        {
            Edits = new[]
            {
                new FixEdit
                {
                    FilePath = "test.py",
                    StartLine = 10,
                    EndLine = 5, // Invalid: end < start
                    OriginalLines = new[] { "x" },
                    Replacement = "y"
                }
            },
            Confidence = 50
        };
        
        var result = FixSuggestionValidator.Validate(suggestion);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("endLine"));
    }
    
    [Fact]
    public void Validate_OverlappingEdits_ReturnsInvalid()
    {
        var suggestion = new FixSuggestion
        {
            Edits = new[]
            {
                new FixEdit
                {
                    FilePath = "test.py",
                    StartLine = 10,
                    EndLine = 15,
                    OriginalLines = new[] { "x" },
                    Replacement = "y"
                },
                new FixEdit
                {
                    FilePath = "test.py",
                    StartLine = 12,
                    EndLine = 18,
                    OriginalLines = new[] { "a" },
                    Replacement = "b"
                }
            },
            Confidence = 50
        };
        
        var result = FixSuggestionValidator.Validate(suggestion);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Overlapping"));
    }
}
