using FsCheck;
using FsCheck.Xunit;
using Tsugix.ContextEngine;
using Tsugix.ContextEngine.Parsers;
using Xunit;

namespace Tsugix.Tests.ContextEngine;

/// <summary>
/// Property-based tests for NodeErrorParser.
/// Feature: tsugix-phase2-context-engine, Property 3: Node.js Parsing Round-Trip
/// Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6
/// </summary>
public class NodeErrorParserTests
{
    private readonly NodeErrorParser _parser = new();
    
    private static Gen<string> GenNodeFilePath()
    {
        return Gen.Elements(
            "/home/user/project/index.js",
            "/app/src/server.js",
            "C:\\Users\\dev\\project\\app.js",
            "./lib/utils.js",
            "main.js"
        );
    }
    
    private static Gen<string> GenFunctionName()
    {
        return Gen.Elements(
            "processRequest",
            "handleError",
            "main",
            "init",
            "callback"
        );
    }
    
    private static Gen<string> GenErrorType()
    {
        return Gen.Elements(
            "TypeError",
            "ReferenceError",
            "SyntaxError",
            "RangeError",
            "Error",
            "AssertionError"
        );
    }
    
    private static Gen<string> GenErrorMessage()
    {
        return Gen.Elements(
            "Cannot read property 'foo' of undefined",
            "x is not defined",
            "Unexpected token",
            "Maximum call stack size exceeded",
            "ENOENT: no such file or directory"
        );
    }
    
    private static Gen<(string Error, string Type, string Message, List<(string Path, int Line, int Col, string Func)> Frames)> GenNodeError()
    {
        return from frameCount in Gen.Choose(1, 5)
               from frames in Gen.ListOf(frameCount,
                   from path in GenNodeFilePath()
                   from line in Gen.Choose(1, 500)
                   from col in Gen.Choose(1, 80)
                   from func in GenFunctionName()
                   select (path, line, col, func))
               from errType in GenErrorType()
               from message in GenErrorMessage()
               let error = BuildNodeError(frames.ToList(), errType, message)
               select (error, errType, message, frames.ToList());
    }
    
    private static string BuildNodeError(List<(string Path, int Line, int Col, string Func)> frames, string errType, string message)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{errType}: {message}");
        
        foreach (var (path, line, col, func) in frames)
        {
            sb.AppendLine($"    at {func} ({path}:{line}:{col})");
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// Property 3: Node.js Parsing Round-Trip
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NodeError_ExtractsAllFrames()
    {
        return Prop.ForAll(
            GenNodeError().ToArbitrary(),
            data =>
            {
                var (error, expectedType, expectedMessage, expectedFrames) = data;
                
                var confidence = _parser.CanParse(error);
                var result = _parser.Parse(error);
                
                var hasHighConfidence = confidence == ParserConfidence.High;
                var parseSuccess = result.Success;
                var correctType = result.Exception?.Type == expectedType;
                var correctMessage = result.Exception?.Message == expectedMessage;
                var correctFrameCount = result.Frames.Count == expectedFrames.Count;
                
                // Check paths, lines, and columns match (function names may vary due to regex)
                var framesMatch = result.Frames.Count == expectedFrames.Count &&
                    result.Frames.Zip(expectedFrames, (actual, expected) =>
                        actual.FilePath == expected.Path &&
                        actual.LineNumber == expected.Line &&
                        actual.ColumnNumber == expected.Col)
                    .All(x => x);
                
                return hasHighConfidence && parseSuccess && correctType && 
                       correctMessage && correctFrameCount && framesMatch;
            });
    }
    
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyInput_ReturnsNoneConfidence(string? input)
    {
        Assert.Equal(ParserConfidence.None, _parser.CanParse(input!));
    }
    
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyInput_ReturnsFailedParse(string? input)
    {
        var result = _parser.Parse(input!);
        Assert.False(result.Success);
    }
    
    [Fact]
    public void V8StackFrame_GivesHighConfidence()
    {
        var stderr = "Error: test\n    at main (/app/index.js:10:5)";
        Assert.Equal(ParserConfidence.High, _parser.CanParse(stderr));
    }
    
    [Theory]
    [InlineData("/app/node_modules/express/lib/router.js")]
    [InlineData("internal/process/task_queues.js")]
    public void LibraryPaths_MarkedAsNonUserCode(string libPath)
    {
        var stderr = $"Error: test\n    at handler ({libPath}:10:5)";
        var result = _parser.Parse(stderr);
        
        Assert.True(result.Success);
        Assert.Single(result.Frames);
        Assert.False(result.Frames[0].IsUserCode);
    }
    
    [Theory]
    [InlineData("/home/user/project/app.js")]
    [InlineData("./src/index.js")]
    public void UserPaths_MarkedAsUserCode(string userPath)
    {
        var stderr = $"Error: test\n    at handler ({userPath}:10:5)";
        var result = _parser.Parse(stderr);
        
        Assert.True(result.Success);
        Assert.Single(result.Frames);
        Assert.True(result.Frames[0].IsUserCode);
    }
    
    [Fact]
    public void RealWorldError_ParsesCorrectly()
    {
        var stderr = @"TypeError: Cannot read property 'name' of undefined
    at processUser (/home/user/project/handlers.js:42:15)
    at handleRequest (/home/user/project/server.js:28:10)";
        
        var result = _parser.Parse(stderr);
        
        Assert.True(result.Success);
        Assert.Equal("TypeError", result.Exception?.Type);
        Assert.Equal("Cannot read property 'name' of undefined", result.Exception?.Message);
        Assert.Equal(2, result.Frames.Count);
        
        Assert.Equal("/home/user/project/handlers.js", result.Frames[0].FilePath);
        Assert.Equal(42, result.Frames[0].LineNumber);
        Assert.Equal(15, result.Frames[0].ColumnNumber);
        Assert.Equal("processUser", result.Frames[0].FunctionName);
    }
    
    [Fact]
    public void WindowsPath_ParsesCorrectly()
    {
        var stderr = "Error: test\n    at handleError (C:\\Users\\dev\\project\\app.js:399:2)";
        
        var result = _parser.Parse(stderr);
        
        Assert.True(result.Success);
        Assert.Single(result.Frames);
        Assert.Equal("C:\\Users\\dev\\project\\app.js", result.Frames[0].FilePath);
        Assert.Equal(399, result.Frames[0].LineNumber);
        Assert.Equal(2, result.Frames[0].ColumnNumber);
    }
    
    [Fact]
    public void SimpleFrames_ParsesCorrectly()
    {
        var stderr = @"Error: Maximum call stack size exceeded
    at init (./lib/utils.js:401:23)
    at main (main.js:442:13)";
        
        var result = _parser.Parse(stderr);
        
        Assert.True(result.Success);
        Assert.Equal("Error", result.Exception?.Type);
        Assert.Equal("Maximum call stack size exceeded", result.Exception?.Message);
        Assert.Equal(2, result.Frames.Count);
        
        Assert.Equal("./lib/utils.js", result.Frames[0].FilePath);
        Assert.Equal(401, result.Frames[0].LineNumber);
        Assert.Equal(23, result.Frames[0].ColumnNumber);
        
        Assert.Equal("main.js", result.Frames[1].FilePath);
        Assert.Equal(442, result.Frames[1].LineNumber);
        Assert.Equal(13, result.Frames[1].ColumnNumber);
    }
}
