using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using Tsugix.AiSurgeon;
using Tsugix.ContextEngine;
using Xunit;

namespace Tsugix.Tests.AiSurgeon;

/// <summary>
/// Tests for PromptBuilder.
/// Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6
/// </summary>
public class PromptBuilderTests
{
    private readonly PromptBuilder _builder = new();
    
    [Fact]
    public void BuildSystemPrompt_ContainsJsonFormat()
    {
        var prompt = _builder.BuildSystemPrompt();
        
        Assert.Contains("JSON", prompt);
        Assert.Contains("filePath", prompt);
        Assert.Contains("originalLines", prompt);
        Assert.Contains("replacement", prompt);
        Assert.Contains("explanation", prompt);
        Assert.Contains("confidence", prompt);
    }
    
    [Fact]
    public void BuildSystemPrompt_ContainsSecurityWarning()
    {
        var prompt = _builder.BuildSystemPrompt();
        
        Assert.Contains("UNTRUSTED", prompt);
        Assert.Contains("IGNORE", prompt);
    }
    
    [Fact]
    public void BuildUserPrompt_ReturnsValidJson()
    {
        var context = CreateTestErrorContext("Python");
        var options = new PromptOptions();
        
        var prompt = _builder.BuildUserPrompt(context, options);
        
        // Should be valid JSON
        var doc = JsonDocument.Parse(prompt);
        Assert.NotNull(doc);
    }
    
    [Fact]
    public void BuildUserPrompt_ContainsLanguage()
    {
        var context = CreateTestErrorContext("Python");
        var options = new PromptOptions();
        
        var prompt = _builder.BuildUserPrompt(context, options);
        var doc = JsonDocument.Parse(prompt);
        
        Assert.Equal("Python", doc.RootElement.GetProperty("language").GetString());
    }
    
    [Fact]
    public void BuildUserPrompt_ContainsExceptionInfo()
    {
        var context = CreateTestErrorContext("Python", "ValueError", "invalid literal");
        var options = new PromptOptions();
        
        var prompt = _builder.BuildUserPrompt(context, options);
        var doc = JsonDocument.Parse(prompt);
        
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal("ValueError", error.GetProperty("type").GetString());
        Assert.Equal("invalid literal", error.GetProperty("message").GetString());
    }
    
    [Fact]
    public void BuildUserPrompt_ContainsStackTrace()
    {
        var context = CreateTestErrorContext("Python");
        var options = new PromptOptions();
        
        var prompt = _builder.BuildUserPrompt(context, options);
        var doc = JsonDocument.Parse(prompt);
        
        var stackTrace = doc.RootElement.GetProperty("stackTrace");
        Assert.True(stackTrace.GetArrayLength() > 0);
        Assert.Equal("test_function", stackTrace[0].GetProperty("functionName").GetString());
    }
    
    [Fact]
    public void BuildUserPrompt_ContainsRawSourceCode()
    {
        var context = CreateTestErrorContext("Python");
        var options = new PromptOptions();
        
        var prompt = _builder.BuildUserPrompt(context, options);
        var doc = JsonDocument.Parse(prompt);
        
        var sourceContext = doc.RootElement.GetProperty("sourceContext");
        Assert.Equal("test.py", sourceContext.GetProperty("filePath").GetString());
        Assert.Equal(3, sourceContext.GetProperty("errorLine").GetInt32());
        
        var rawCode = sourceContext.GetProperty("rawCode").GetString();
        Assert.NotNull(rawCode);
        // Raw code should NOT contain line number prefixes or decorations
        Assert.DoesNotContain(">>>", rawCode);
        Assert.DoesNotContain("Error on this line", rawCode);
        Assert.Contains("def test_function():", rawCode);
    }
    
    [Fact]
    public void BuildUserPrompt_RawCodePreservesWhitespace()
    {
        var context = CreateTestErrorContext("Python");
        var options = new PromptOptions();
        
        var prompt = _builder.BuildUserPrompt(context, options);
        var doc = JsonDocument.Parse(prompt);
        
        var rawCode = doc.RootElement.GetProperty("sourceContext").GetProperty("rawCode").GetString();
        
        // Should preserve indentation
        Assert.Contains("    x = 'hello'", rawCode);
        Assert.Contains("    return int(x)", rawCode);
    }
    
    [Fact]
    public void BuildUserPrompt_TruncatesLongMessages()
    {
        var longMessage = new string('x', 1000);
        var context = CreateTestErrorContext("Python", "Error", longMessage);
        var options = new PromptOptions();
        
        var prompt = _builder.BuildUserPrompt(context, options);
        var doc = JsonDocument.Parse(prompt);
        
        var message = doc.RootElement.GetProperty("error").GetProperty("message").GetString();
        Assert.True(message!.Length <= 503); // 500 + "..."
        Assert.EndsWith("...", message);
    }
    
    [Fact]
    public void BuildUserPrompt_LimitsStackFrames()
    {
        var context = CreateLargeErrorContext();
        var options = new PromptOptions();
        
        var prompt = _builder.BuildUserPrompt(context, options);
        var doc = JsonDocument.Parse(prompt);
        
        var stackTrace = doc.RootElement.GetProperty("stackTrace");
        Assert.True(stackTrace.GetArrayLength() <= 20); // MaxStackFrames
    }
    
    private static ErrorContext CreateTestErrorContext(
        string language = "Python",
        string exceptionType = "ValueError",
        string message = "Test error message")
    {
        var sourceLines = new List<SourceLine>
        {
            new() { LineNumber = 1, Content = "def test_function():" },
            new() { LineNumber = 2, Content = "    x = 'hello'" },
            new() { LineNumber = 3, Content = "    return int(x)", IsErrorLine = true },
            new() { LineNumber = 4, Content = "" }
        };
        
        var snippet = new SourceSnippet
        {
            FilePath = "test.py",
            StartLine = 1,
            EndLine = 4,
            ErrorLine = 3,
            Lines = sourceLines
        };
        
        var frame = new StackFrame
        {
            FilePath = "test.py",
            LineNumber = 3,
            FunctionName = "test_function",
            IsUserCode = true,
            SourceContext = snippet
        };
        
        return new ErrorContext
        {
            Language = language,
            Exception = new ExceptionInfo
            {
                Type = exceptionType,
                Message = message
            },
            Frames = new[] { frame },
            PrimaryFrame = frame,
            OriginalCommand = "python test.py",
            Timestamp = DateTimeOffset.UtcNow,
            WorkingDirectory = "/test"
        };
    }
    
    private static ErrorContext CreateLargeErrorContext()
    {
        var sourceLines = new List<SourceLine>();
        for (var i = 1; i <= 100; i++)
        {
            sourceLines.Add(new SourceLine
            {
                LineNumber = i,
                Content = $"line {i}: " + new string('x', 100),
                IsErrorLine = i == 50
            });
        }
        
        var snippet = new SourceSnippet
        {
            FilePath = "large_file.py",
            StartLine = 1,
            EndLine = 100,
            ErrorLine = 50,
            Lines = sourceLines
        };
        
        var frames = new List<StackFrame>();
        for (var i = 0; i < 100; i++)
        {
            frames.Add(new StackFrame
            {
                FilePath = $"file{i}.py",
                LineNumber = i * 10,
                FunctionName = $"function_{i}",
                ClassName = $"Class{i}",
                IsUserCode = true,
                SourceContext = i == 0 ? snippet : null
            });
        }
        
        return new ErrorContext
        {
            Language = "Python",
            Exception = new ExceptionInfo
            {
                Type = "RuntimeError",
                Message = "Large error with lots of context"
            },
            Frames = frames,
            PrimaryFrame = frames[0],
            OriginalCommand = "python large_file.py",
            Timestamp = DateTimeOffset.UtcNow,
            WorkingDirectory = "/test"
        };
    }
}

/// <summary>
/// Property-based tests for PromptBuilder.
/// Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6
/// </summary>
public class PromptBuilderPropertyTests
{
    private readonly PromptBuilder _builder = new();
    
    /// <summary>
    /// Property 1: Prompt Completeness
    /// For any valid ErrorContext, the generated prompt SHALL contain:
    /// (a) the exception type and message, (b) the source code context,
    /// (c) the stack trace information, (d) the JSON response format specification,
    /// and (e) the programming language.
    /// Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5
    /// </summary>
    [Property(MaxTest = 50)]
    public Property PromptCompleteness()
    {
        var languageGen = Gen.Elements("Python", "JavaScript", "C#", "Java", "Go", "Rust", "Ruby", "PHP", "Swift");
        var exceptionTypeGen = Gen.Elements("ValueError", "TypeError", "NullReferenceException", "RuntimeError");
        var messageGen = Arb.From<NonEmptyString>().Generator.Select(s => s.Get);
        
        var contextGen = from language in languageGen
                         from exceptionType in exceptionTypeGen
                         from message in messageGen
                         select CreateErrorContext(language, exceptionType, message);
        
        return Prop.ForAll(
            contextGen.ToArbitrary(),
            context =>
            {
                var systemPrompt = _builder.BuildSystemPrompt();
                var userPrompt = _builder.BuildUserPrompt(context, new PromptOptions());
                
                // User prompt should be valid JSON
                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(userPrompt);
                }
                catch
                {
                    return false;
                }
                
                // (a) Exception type and message
                var error = doc.RootElement.GetProperty("error");
                var hasExceptionType = error.GetProperty("type").GetString() == context.Exception.Type;
                var hasMessage = error.GetProperty("message").GetString()?.Contains(
                    context.Exception.Message.Length > 500 
                        ? context.Exception.Message[..497] 
                        : context.Exception.Message) ?? false;
                
                // (b) Source code context (if available)
                var hasSourceContext = context.Frames.All(f => f.SourceContext == null) ||
                                       doc.RootElement.TryGetProperty("sourceContext", out _);
                
                // (c) Stack trace information
                var hasStackTrace = context.Frames.Count == 0 ||
                                    doc.RootElement.TryGetProperty("stackTrace", out var st) && st.GetArrayLength() > 0;
                
                // (d) JSON response format in system prompt
                var hasJsonFormat = systemPrompt.Contains("JSON") &&
                                    systemPrompt.Contains("filePath") &&
                                    systemPrompt.Contains("originalLines");
                
                // (e) Programming language
                var hasLanguage = doc.RootElement.GetProperty("language").GetString() == context.Language;
                
                return hasExceptionType && hasMessage && hasSourceContext && 
                       hasStackTrace && hasJsonFormat && hasLanguage;
            });
    }
    
    /// <summary>
    /// Property 2: Prompt Size Bounded
    /// For any ErrorContext regardless of source file size or stack trace depth,
    /// the generated prompt SHALL not exceed reasonable limits.
    /// Validates: Requirements 2.6
    /// </summary>
    [Property(MaxTest = 30)]
    public Property PromptSizeBounded()
    {
        var lineSizeGen = Gen.Choose(10, 500);
        var lineCountGen = Gen.Choose(1, 200);
        var frameCountGen = Gen.Choose(1, 100);
        
        var testGen = from lineSize in lineSizeGen
                      from lineCount in lineCountGen
                      from frameCount in frameCountGen
                      select (lineSize, lineCount, frameCount);
        
        return Prop.ForAll(
            testGen.ToArbitrary(),
            tuple =>
            {
                var (lineSize, lineCount, frameCount) = tuple;
                var context = CreateLargeErrorContext(lineCount, lineSize, frameCount);
                var options = new PromptOptions();
                
                var prompt = _builder.BuildUserPrompt(context, options);
                
                // Should be bounded regardless of input size
                // Max: 50 lines * ~500 chars + 20 frames * ~100 chars + overhead
                const int maxExpectedSize = 50000;
                
                return prompt.Length <= maxExpectedSize;
            });
    }
    
    /// <summary>
    /// Property 3: Raw Code Not Decorated
    /// For any ErrorContext with source code, the raw code in the prompt
    /// SHALL NOT contain line number prefixes or error markers.
    /// </summary>
    [Property(MaxTest = 30)]
    public Property RawCodeNotDecorated()
    {
        var contextGen = Gen.Constant(CreateErrorContextWithSource());
        
        return Prop.ForAll(
            contextGen.ToArbitrary(),
            context =>
            {
                var prompt = _builder.BuildUserPrompt(context, new PromptOptions());
                var doc = JsonDocument.Parse(prompt);
                
                if (!doc.RootElement.TryGetProperty("sourceContext", out var sourceContext))
                    return true; // No source context is fine
                
                var rawCode = sourceContext.GetProperty("rawCode").GetString();
                if (string.IsNullOrEmpty(rawCode))
                    return true;
                
                // Should NOT contain decorations
                var noLineNumberPrefix = !rawCode.Contains(">>> ");
                var noErrorMarker = !rawCode.Contains("Error on this line");
                var noNumberedLines = !System.Text.RegularExpressions.Regex.IsMatch(rawCode, @"^\s*\d+:\s", System.Text.RegularExpressions.RegexOptions.Multiline);
                
                return noLineNumberPrefix && noErrorMarker && noNumberedLines;
            });
    }
    
    private static ErrorContext CreateErrorContext(string language, string exceptionType, string message)
    {
        var sourceLines = new List<SourceLine>
        {
            new() { LineNumber = 1, Content = "line 1" },
            new() { LineNumber = 2, Content = "line 2", IsErrorLine = true },
            new() { LineNumber = 3, Content = "line 3" }
        };
        
        var snippet = new SourceSnippet
        {
            FilePath = "test.py",
            StartLine = 1,
            EndLine = 3,
            ErrorLine = 2,
            Lines = sourceLines
        };
        
        var frame = new StackFrame
        {
            FilePath = "test.py",
            LineNumber = 2,
            FunctionName = "test_function",
            IsUserCode = true,
            SourceContext = snippet
        };
        
        return new ErrorContext
        {
            Language = language,
            Exception = new ExceptionInfo
            {
                Type = exceptionType,
                Message = message
            },
            Frames = new[] { frame },
            PrimaryFrame = frame,
            OriginalCommand = "test command",
            Timestamp = DateTimeOffset.UtcNow,
            WorkingDirectory = "/test"
        };
    }
    
    private static ErrorContext CreateErrorContextWithSource()
    {
        var sourceLines = new List<SourceLine>
        {
            new() { LineNumber = 10, Content = "def calculate(x):" },
            new() { LineNumber = 11, Content = "    return x / 0", IsErrorLine = true },
            new() { LineNumber = 12, Content = "" }
        };
        
        var snippet = new SourceSnippet
        {
            FilePath = "math.py",
            StartLine = 10,
            EndLine = 12,
            ErrorLine = 11,
            Lines = sourceLines
        };
        
        var frame = new StackFrame
        {
            FilePath = "math.py",
            LineNumber = 11,
            FunctionName = "calculate",
            IsUserCode = true,
            SourceContext = snippet
        };
        
        return new ErrorContext
        {
            Language = "Python",
            Exception = new ExceptionInfo
            {
                Type = "ZeroDivisionError",
                Message = "division by zero"
            },
            Frames = new[] { frame },
            PrimaryFrame = frame,
            OriginalCommand = "python math.py",
            Timestamp = DateTimeOffset.UtcNow,
            WorkingDirectory = "/test"
        };
    }
    
    private static ErrorContext CreateLargeErrorContext(int lineCount, int lineSize, int frameCount)
    {
        var sourceLines = new List<SourceLine>();
        for (var i = 1; i <= lineCount; i++)
        {
            sourceLines.Add(new SourceLine
            {
                LineNumber = i,
                Content = new string('x', lineSize),
                IsErrorLine = i == lineCount / 2
            });
        }
        
        var snippet = new SourceSnippet
        {
            FilePath = "large_file.py",
            StartLine = 1,
            EndLine = lineCount,
            ErrorLine = lineCount / 2,
            Lines = sourceLines
        };
        
        var frames = new List<StackFrame>();
        for (var i = 0; i < frameCount; i++)
        {
            frames.Add(new StackFrame
            {
                FilePath = $"file{i}.py",
                LineNumber = i * 10,
                FunctionName = $"function_{i}",
                ClassName = $"Class{i}",
                IsUserCode = true,
                SourceContext = i == 0 ? snippet : null
            });
        }
        
        return new ErrorContext
        {
            Language = "Python",
            Exception = new ExceptionInfo
            {
                Type = "RuntimeError",
                Message = "Large error"
            },
            Frames = frames,
            PrimaryFrame = frames[0],
            OriginalCommand = "python large_file.py",
            Timestamp = DateTimeOffset.UtcNow,
            WorkingDirectory = "/test"
        };
    }
}
