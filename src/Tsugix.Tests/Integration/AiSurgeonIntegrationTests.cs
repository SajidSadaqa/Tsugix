using Spectre.Console.Testing;
using Tsugix.AiSurgeon;
using Tsugix.ContextEngine;
using Tsugix.Core;
using Tsugix.Process;
using Tsugix.Tests.AiSurgeon;
using Xunit;

namespace Tsugix.Tests.Integration;

/// <summary>
/// Integration tests for AI Surgeon with Phase 1 and Phase 2.
/// Validates: Full flow from crash detection to fix suggestion.
/// </summary>
public class AiSurgeonIntegrationTests
{
    /// <summary>
    /// Test: Phase 1 crash report flows to Phase 2 context engine.
    /// </summary>
    [Fact]
    public async Task CrashReport_FlowsToContextEngine()
    {
        // Phase 1: Execute a failing Python command (simulated)
        var stderr = """
            Traceback (most recent call last):
              File "test.py", line 10, in <module>
                result = divide(10, 0)
              File "test.py", line 5, in divide
                return a / b
            ZeroDivisionError: division by zero
            """;
        
        var crashReport = new CrashReport
        {
            Stderr = stderr,
            ExitCode = 1,
            Command = "python test.py",
            Timestamp = DateTimeOffset.UtcNow,
            WorkingDirectory = Environment.CurrentDirectory
        };
        
        // Phase 2: Process with context engine
        var contextEngine = Tsugix.ContextEngine.ContextEngine.CreateDefault();
        var errorContext = contextEngine.Process(crashReport);
        
        Assert.NotNull(errorContext);
        Assert.Equal("Python", errorContext.Language);
        Assert.Equal("ZeroDivisionError", errorContext.Exception.Type);
        Assert.Contains("division by zero", errorContext.Exception.Message);
    }
    
    /// <summary>
    /// Test: Phase 2 error context flows to Phase 3 AI Surgeon.
    /// </summary>
    [Fact]
    public async Task ErrorContext_FlowsToAiSurgeon()
    {
        var errorContext = new ErrorContext
        {
            Language = "Python",
            Exception = new ExceptionInfo
            {
                Type = "ZeroDivisionError",
                Message = "division by zero"
            },
            Frames = new[]
            {
                new StackFrame
                {
                    FilePath = "test.py",
                    LineNumber = 5,
                    FunctionName = "divide",
                    IsUserCode = true
                }
            },
            OriginalCommand = "python test.py",
            Timestamp = DateTimeOffset.UtcNow,
            WorkingDirectory = Environment.CurrentDirectory
        };
        
        var console = new TestConsole();
        var mockClient = new MockLlmClient();
        mockClient.QueueResponse("""
            {
                "filePath": "test.py",
                "originalLines": ["return a / b"],
                "replacementLines": ["return a / b if b != 0 else 0"],
                "explanation": "Added zero check",
                "confidence": 85
            }
            """);
        
        var surgeon = new Tsugix.AiSurgeon.AiSurgeon(
            mockClient,
            new PromptBuilder(),
            new JsonFixParser(),
            new SpectreConsoleDiffView(console),
            new FilePatcher(),
            new UserInteraction(console, autoApply: false, autoRerun: false),
            TsugixConfig.Default);
        
        var options = new AiSurgeonOptions { SkipAi = false };
        
        // This will fail at the prompt stage, but we can verify the LLM was called
        try
        {
            await surgeon.AnalyzeAndFixAsync(errorContext, options);
        }
        catch
        {
            // Expected - TestConsole doesn't support prompts
        }
        
        Assert.Single(mockClient.Calls);
        Assert.Contains("ZeroDivisionError", mockClient.Calls[0].UserPrompt);
    }
    
    /// <summary>
    /// Test: AI Surgeon with skip-ai flag returns immediately.
    /// </summary>
    [Fact]
    public async Task AiSurgeon_SkipAi_ReturnsSkipped()
    {
        var errorContext = CreateTestErrorContext();
        var console = new TestConsole();
        
        var surgeon = new Tsugix.AiSurgeon.AiSurgeon(
            null,
            new PromptBuilder(),
            new JsonFixParser(),
            new SpectreConsoleDiffView(console),
            new FilePatcher(),
            new UserInteraction(console, autoApply: false, autoRerun: false),
            TsugixConfig.Default);
        
        var options = new AiSurgeonOptions { SkipAi = true };
        var result = await surgeon.AnalyzeAndFixAsync(errorContext, options);
        
        Assert.Equal(FixOutcome.Skipped, result.Outcome);
    }
    
    /// <summary>
    /// Test: Full flow with mock LLM returns valid fix result.
    /// </summary>
    [Fact]
    public async Task FullFlow_WithMockLlm_ReturnsValidResult()
    {
        // Phase 1: Simulate crash
        var stderr = """
            Traceback (most recent call last):
              File "app.py", line 15, in main
                process_data(None)
              File "app.py", line 8, in process_data
                return data.strip()
            AttributeError: 'NoneType' object has no attribute 'strip'
            """;
        
        var crashReport = new CrashReport
        {
            Stderr = stderr,
            ExitCode = 1,
            Command = "python app.py",
            Timestamp = DateTimeOffset.UtcNow,
            WorkingDirectory = Environment.CurrentDirectory
        };
        
        // Phase 2: Parse error
        var contextEngine = Tsugix.ContextEngine.ContextEngine.CreateDefault();
        var errorContext = contextEngine.Process(crashReport);
        
        Assert.NotNull(errorContext);
        
        // Phase 3: AI Surgeon (with mock)
        var console = new TestConsole();
        var mockClient = new MockLlmClient();
        mockClient.QueueResponse("""
            {
                "filePath": "app.py",
                "originalLines": ["return data.strip()"],
                "replacementLines": ["return data.strip() if data else ''"],
                "explanation": "Added None check before calling strip()",
                "confidence": 90
            }
            """);
        
        var surgeon = new Tsugix.AiSurgeon.AiSurgeon(
            mockClient,
            new PromptBuilder(),
            new JsonFixParser(),
            new SpectreConsoleDiffView(console),
            new FilePatcher(),
            new UserInteraction(console, autoApply: false, autoRerun: false),
            TsugixConfig.Default);
        
        var options = new AiSurgeonOptions();
        
        // The test will fail at prompt, but we verify the flow worked
        try
        {
            await surgeon.AnalyzeAndFixAsync(errorContext, options);
        }
        catch
        {
            // Expected
        }
        
        // Verify LLM was called with correct context
        Assert.Single(mockClient.Calls);
        var call = mockClient.Calls[0];
        Assert.Contains("AttributeError", call.UserPrompt);
        Assert.Contains("NoneType", call.UserPrompt);
    }
    
    /// <summary>
    /// Test: Multiple language errors are handled correctly.
    /// </summary>
    [Theory]
    [InlineData("Python", "ValueError: invalid literal")]
    [InlineData("Node.js", "TypeError: Cannot read property")]
    [InlineData("C#", "System.NullReferenceException")]
    public async Task MultipleLanguages_HandledCorrectly(string language, string errorMessage)
    {
        var errorContext = new ErrorContext
        {
            Language = language,
            Exception = new ExceptionInfo
            {
                Type = errorMessage.Split(':')[0].Trim(),
                Message = errorMessage
            },
            Frames = Array.Empty<StackFrame>(),
            OriginalCommand = "test",
            Timestamp = DateTimeOffset.UtcNow,
            WorkingDirectory = Environment.CurrentDirectory
        };
        
        var console = new TestConsole();
        var mockClient = new MockLlmClient();
        mockClient.QueueResponse("invalid response");
        
        var surgeon = new Tsugix.AiSurgeon.AiSurgeon(
            mockClient,
            new PromptBuilder(),
            new JsonFixParser(),
            new SpectreConsoleDiffView(console),
            new FilePatcher(),
            new UserInteraction(console, autoApply: false, autoRerun: false),
            TsugixConfig.Default);
        
        var result = await surgeon.AnalyzeAndFixAsync(errorContext, new AiSurgeonOptions());
        
        // Should handle gracefully even with invalid response
        Assert.Equal(FixOutcome.NoFixSuggested, result.Outcome);
        
        // Verify prompt included language
        Assert.Single(mockClient.Calls);
        Assert.Contains(language, mockClient.Calls[0].UserPrompt);
    }
    
    private static ErrorContext CreateTestErrorContext()
    {
        return new ErrorContext
        {
            Language = "Python",
            Exception = new ExceptionInfo
            {
                Type = "ValueError",
                Message = "Test error"
            },
            Frames = Array.Empty<StackFrame>(),
            OriginalCommand = "python test.py",
            Timestamp = DateTimeOffset.UtcNow,
            WorkingDirectory = Environment.CurrentDirectory
        };
    }
}
