using FsCheck;
using FsCheck.Xunit;
using Tsugix.ContextEngine;
using Tsugix.Core;
using Xunit;

namespace Tsugix.Tests.ContextEngine;

/// <summary>
/// Property-based tests for ContextEngine.
/// Feature: tsugix-phase2-context-engine, Property 12: ErrorContext Completeness
/// Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5
/// </summary>
public class ContextEngineTests
{
    private readonly Tsugix.ContextEngine.ContextEngine _engine = Tsugix.ContextEngine.ContextEngine.CreateDefault();
    
    /// <summary>
    /// Property 12: ErrorContext Completeness
    /// For any valid error input, the ErrorContext should contain all required fields.
    /// </summary>
    [Property(MaxTest = 50)]
    public Property ErrorContext_ContainsAllRequiredFields()
    {
        var errorSamples = new[]
        {
            "Traceback (most recent call last):\n  File \"test.py\", line 10, in main\nValueError: test",
            "TypeError: Cannot read property 'x' of undefined\n    at main (/app/index.js:10:5)",
            "System.NullReferenceException: Object reference\n   at MyApp.Main() in C:\\app\\Program.cs:line 10",
            "java.lang.NullPointerException: test\n\tat Main.run(Main.java:10)"
        };
        
        return Prop.ForAll(
            Gen.Elements(errorSamples).ToArbitrary(),
            stderr =>
            {
                var result = _engine.ProcessStderr(stderr);
                
                if (result == null)
                    return false.Label("Result was null");
                
                var hasLanguage = !string.IsNullOrEmpty(result.Language);
                var hasException = result.Exception != null;
                var hasFrames = result.Frames != null;
                var hasCommand = !string.IsNullOrEmpty(result.OriginalCommand);
                var hasWorkingDir = !string.IsNullOrEmpty(result.WorkingDirectory);
                
                return (hasLanguage && hasException && hasFrames && hasCommand && hasWorkingDir)
                    .Label($"Language: {hasLanguage}, Exception: {hasException}, Frames: {hasFrames}");
            });
    }
    
    [Fact]
    public void Process_PythonError_ReturnsCorrectContext()
    {
        var stderr = @"Traceback (most recent call last):
  File ""main.py"", line 42, in main
    result = process()
ValueError: invalid value";
        
        var result = _engine.ProcessStderr(stderr);
        
        Assert.NotNull(result);
        Assert.Equal("Python", result.Language);
        Assert.Equal("ValueError", result.Exception.Type);
        Assert.Equal("invalid value", result.Exception.Message);
        Assert.Single(result.Frames);
        Assert.Equal("main.py", result.Frames[0].FilePath);
        Assert.Equal(42, result.Frames[0].LineNumber);
    }
    
    [Fact]
    public void Process_NodeError_ReturnsCorrectContext()
    {
        var stderr = @"TypeError: Cannot read property 'name' of undefined
    at processUser (/app/handlers.js:42:15)
    at handleRequest (/app/server.js:28:10)";
        
        var result = _engine.ProcessStderr(stderr);
        
        Assert.NotNull(result);
        Assert.Equal("Node.js", result.Language);
        Assert.Equal("TypeError", result.Exception.Type);
        Assert.Equal(2, result.Frames.Count);
    }
    
    [Fact]
    public void Process_CSharpError_ReturnsCorrectContext()
    {
        var stderr = @"System.NullReferenceException: Object reference not set
   at MyApp.Service.GetUser() in C:\src\Service.cs:line 42";
        
        var result = _engine.ProcessStderr(stderr);
        
        Assert.NotNull(result);
        Assert.Equal("C#", result.Language);
        Assert.Equal("System.NullReferenceException", result.Exception.Type);
    }
    
    [Fact]
    public void Process_CrashReport_ReturnsContext()
    {
        var crashReport = new CrashReport
        {
            Command = "python main.py",
            ExitCode = 1,
            Stderr = "Traceback (most recent call last):\n  File \"main.py\", line 1\nValueError: test",
            WorkingDirectory = Directory.GetCurrentDirectory(),
            Timestamp = DateTimeOffset.Now
        };
        
        var result = _engine.Process(crashReport);
        
        Assert.NotNull(result);
        Assert.Equal("Python", result.Language);
        Assert.Equal("python main.py", result.OriginalCommand);
    }
    
    [Fact]
    public void Process_EmptyStderr_ReturnsNull()
    {
        Assert.Null(_engine.ProcessStderr(""));
        Assert.Null(_engine.ProcessStderr(null!));
    }
    
    [Fact]
    public void Process_UnknownFormat_ReturnsFallback()
    {
        var stderr = "Some random error message that doesn't match any pattern";
        
        var result = _engine.ProcessStderr(stderr);
        
        Assert.NotNull(result);
        Assert.Equal("Unknown", result.Language);
        Assert.Equal("Error", result.Exception.Type);
    }
    
    [Fact]
    public void CreateDefault_RegistersAllParsers()
    {
        var engine = Tsugix.ContextEngine.ContextEngine.CreateDefault();
        var registry = ParserRegistry.CreateWithDefaultParsers();
        
        // Should have 9 parsers registered
        Assert.Equal(9, registry.Count);
    }
    
    /// <summary>
    /// Property 14: Graceful Degradation
    /// The engine should handle malformed input gracefully.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GracefulDegradation_NoExceptions()
    {
        return Prop.ForAll(
            Arb.From<string>(),
            input =>
            {
                try
                {
                    var result = _engine.ProcessStderr(input);
                    // Should either return null or a valid context, never throw
                    return (result == null || result.Language != null)
                        .Label("Should not throw");
                }
                catch
                {
                    return false.Label("Exception thrown");
                }
            });
    }
}
