using FsCheck;
using FsCheck.Xunit;
using Tsugix.ContextEngine;
using Tsugix.ContextEngine.Parsers;
using Xunit;

namespace Tsugix.Tests.ContextEngine;

/// <summary>
/// Property-based tests for PythonErrorParser.
/// Feature: tsugix-phase2-context-engine, Property 2: Python Parsing Round-Trip
/// Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6
/// </summary>
public class PythonErrorParserTests
{
    private readonly PythonErrorParser _parser = new();
    
    /// <summary>
    /// Generates valid Python file paths.
    /// </summary>
    private static Gen<string> GenPythonFilePath()
    {
        return Gen.Elements(
            "/home/user/project/main.py",
            "/app/src/module.py",
            "C:\\Users\\dev\\project\\script.py",
            "./relative/path.py",
            "simple.py"
        );
    }
    
    /// <summary>
    /// Generates valid Python function names.
    /// </summary>
    private static Gen<string> GenFunctionName()
    {
        return Gen.Elements(
            "<module>",
            "main",
            "process_data",
            "__init__",
            "MyClass.method",
            "lambda"
        );
    }
    
    /// <summary>
    /// Generates valid Python exception types.
    /// </summary>
    private static Gen<string> GenExceptionType()
    {
        return Gen.Elements(
            "ValueError",
            "TypeError",
            "KeyError",
            "IndexError",
            "AttributeError",
            "RuntimeError",
            "FileNotFoundError",
            "ZeroDivisionError",
            "NameError",
            "ImportError"
        );
    }
    
    /// <summary>
    /// Generates valid exception messages.
    /// </summary>
    private static Gen<string> GenExceptionMessage()
    {
        return Gen.Elements(
            "invalid literal for int() with base 10: 'abc'",
            "'NoneType' object has no attribute 'foo'",
            "list index out of range",
            "division by zero",
            "name 'undefined' is not defined",
            "No module named 'missing'"
        );
    }

    /// <summary>
    /// Generates a synthetic Python traceback.
    /// </summary>
    private static Gen<(string Traceback, string ExceptionType, string Message, List<(string Path, int Line, string Func)> Frames)> GenPythonTraceback()
    {
        return from frameCount in Gen.Choose(1, 5)
               from frames in Gen.ListOf(frameCount, 
                   from path in GenPythonFilePath()
                   from line in Gen.Choose(1, 1000)
                   from func in GenFunctionName()
                   select (path, line, func))
               from exType in GenExceptionType()
               from message in GenExceptionMessage()
               let traceback = BuildTraceback(frames.ToList(), exType, message)
               select (traceback, exType, message, frames.ToList());
    }
    
    private static string BuildTraceback(List<(string Path, int Line, string Func)> frames, string exType, string message)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Traceback (most recent call last):");
        
        foreach (var (path, line, func) in frames)
        {
            sb.AppendLine($"  File \"{path}\", line {line}, in {func}");
            sb.AppendLine("    some_code_here()");
        }
        
        sb.AppendLine($"{exType}: {message}");
        return sb.ToString();
    }
    
    /// <summary>
    /// Property 2: Python Parsing Round-Trip
    /// For any valid Python traceback, the parser SHALL extract:
    /// - All stack frames with correct file paths and line numbers
    /// - The exception type and message
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PythonTraceback_ExtractsAllFrames()
    {
        return Prop.ForAll(
            GenPythonTraceback().ToArbitrary(),
            data =>
            {
                var (traceback, expectedType, expectedMessage, expectedFrames) = data;
                
                var confidence = _parser.CanParse(traceback);
                var result = _parser.Parse(traceback);
                
                // Should have high confidence for valid traceback
                var hasHighConfidence = confidence == ParserConfidence.High;
                
                // Should parse successfully
                var parseSuccess = result.Success;
                
                // Should extract correct exception type
                var correctType = result.Exception?.Type == expectedType;
                
                // Should extract correct message
                var correctMessage = result.Exception?.Message == expectedMessage;
                
                // Should extract all frames
                var correctFrameCount = result.Frames.Count == expectedFrames.Count;
                
                // Each frame should have correct path and line
                var framesMatch = result.Frames.Count == expectedFrames.Count &&
                    result.Frames.Zip(expectedFrames, (actual, expected) =>
                        actual.FilePath == expected.Path && 
                        actual.LineNumber == expected.Line &&
                        actual.FunctionName == expected.Func)
                    .All(x => x);
                
                return hasHighConfidence && parseSuccess && correctType && 
                       correctMessage && correctFrameCount && framesMatch;
            });
    }
    
    /// <summary>
    /// Property: Empty/null input returns None confidence.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyInput_ReturnsNoneConfidence(string? input)
    {
        Assert.Equal(ParserConfidence.None, _parser.CanParse(input!));
    }
    
    /// <summary>
    /// Property: Empty/null input returns failed parse result.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyInput_ReturnsFailedParse(string? input)
    {
        var result = _parser.Parse(input!);
        Assert.False(result.Success);
    }
    
    /// <summary>
    /// Property: Traceback header gives high confidence.
    /// </summary>
    [Fact]
    public void TracebackHeader_GivesHighConfidence()
    {
        var stderr = "Traceback (most recent call last):\n  File \"test.py\", line 1\nValueError: test";
        Assert.Equal(ParserConfidence.High, _parser.CanParse(stderr));
    }
    
    /// <summary>
    /// Property: File pattern without header gives medium confidence.
    /// </summary>
    [Fact]
    public void FilePatternOnly_GivesMediumConfidence()
    {
        var stderr = "  File \"test.py\", line 42, in main\nSomeError";
        Assert.Equal(ParserConfidence.Medium, _parser.CanParse(stderr));
    }
    
    /// <summary>
    /// Property: Library paths are marked as non-user code.
    /// </summary>
    [Theory]
    [InlineData("/usr/lib/python3.9/site-packages/requests/api.py")]
    [InlineData("C:\\Python39\\Lib\\site-packages\\numpy\\core.py")]
    [InlineData("<frozen importlib._bootstrap>")]
    public void LibraryPaths_MarkedAsNonUserCode(string libPath)
    {
        var stderr = $"Traceback (most recent call last):\n  File \"{libPath}\", line 10, in func\nValueError: test";
        var result = _parser.Parse(stderr);
        
        Assert.True(result.Success);
        Assert.Single(result.Frames);
        Assert.False(result.Frames[0].IsUserCode);
    }
    
    /// <summary>
    /// Property: User paths are marked as user code.
    /// </summary>
    [Theory]
    [InlineData("/home/user/project/main.py")]
    [InlineData("./src/app.py")]
    [InlineData("script.py")]
    public void UserPaths_MarkedAsUserCode(string userPath)
    {
        var stderr = $"Traceback (most recent call last):\n  File \"{userPath}\", line 10, in func\nValueError: test";
        var result = _parser.Parse(stderr);
        
        Assert.True(result.Success);
        Assert.Single(result.Frames);
        Assert.True(result.Frames[0].IsUserCode);
    }
    
    /// <summary>
    /// Property: Real-world Python traceback parses correctly.
    /// </summary>
    [Fact]
    public void RealWorldTraceback_ParsesCorrectly()
    {
        var stderr = @"Traceback (most recent call last):
  File ""/home/user/project/main.py"", line 42, in main
    result = process_data(data)
  File ""/home/user/project/utils.py"", line 15, in process_data
    return int(value)
ValueError: invalid literal for int() with base 10: 'abc'";
        
        var result = _parser.Parse(stderr);
        
        Assert.True(result.Success);
        Assert.Equal("ValueError", result.Exception?.Type);
        Assert.Equal("invalid literal for int() with base 10: 'abc'", result.Exception?.Message);
        Assert.Equal(2, result.Frames.Count);
        
        Assert.Equal("/home/user/project/main.py", result.Frames[0].FilePath);
        Assert.Equal(42, result.Frames[0].LineNumber);
        Assert.Equal("main", result.Frames[0].FunctionName);
        
        Assert.Equal("/home/user/project/utils.py", result.Frames[1].FilePath);
        Assert.Equal(15, result.Frames[1].LineNumber);
        Assert.Equal("process_data", result.Frames[1].FunctionName);
    }
}
