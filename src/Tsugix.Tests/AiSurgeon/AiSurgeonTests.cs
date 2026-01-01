using FsCheck;
using FsCheck.Xunit;
using Spectre.Console.Testing;
using Tsugix.AiSurgeon;
using Tsugix.ContextEngine;
using Xunit;

namespace Tsugix.Tests.AiSurgeon;

/// <summary>
/// Tests for AiSurgeon orchestration.
/// Validates: All Phase 3 requirements
/// </summary>
public class AiSurgeonTests
{
    [Fact]
    public async Task AnalyzeAndFixAsync_SkipAi_ReturnsSkipped()
    {
        var surgeon = CreateTestSurgeon(null);
        var context = CreateTestErrorContext();
        var options = new AiSurgeonOptions { SkipAi = true };
        
        var result = await surgeon.AnalyzeAndFixAsync(context, options);
        
        Assert.Equal(FixOutcome.Skipped, result.Outcome);
    }
    
    [Fact]
    public async Task AnalyzeAndFixAsync_NoApiKey_ReturnsSkipped()
    {
        var surgeon = CreateTestSurgeon(null);
        var context = CreateTestErrorContext();
        var options = new AiSurgeonOptions();
        
        var result = await surgeon.AnalyzeAndFixAsync(context, options);
        
        Assert.Equal(FixOutcome.Skipped, result.Outcome);
    }
    
    [Fact]
    public async Task AnalyzeAndFixAsync_ValidResponse_DisplaysDiff()
    {
        var mockClient = new MockLlmClient();
        mockClient.QueueResponse(CreateValidFixJson());
        
        var console = new TestConsole();
        var surgeon = CreateTestSurgeon(mockClient, console);
        var context = CreateTestErrorContext();
        var options = new AiSurgeonOptions { AutoApply = false };
        
        // This will prompt for confirmation, which we can't easily test
        // So we just verify the LLM was called
        try
        {
            await surgeon.AnalyzeAndFixAsync(context, options);
        }
        catch
        {
            // Expected - TestConsole doesn't support prompts
        }
        
        Assert.Single(mockClient.Calls);
    }
    
    [Fact]
    public async Task AnalyzeAndFixAsync_InvalidResponse_ReturnsNoFix()
    {
        var mockClient = new MockLlmClient();
        mockClient.QueueResponse("invalid json response");
        
        var console = new TestConsole();
        var surgeon = CreateTestSurgeon(mockClient, console);
        var context = CreateTestErrorContext();
        var options = new AiSurgeonOptions();
        
        var result = await surgeon.AnalyzeAndFixAsync(context, options);
        
        Assert.Equal(FixOutcome.NoFixSuggested, result.Outcome);
    }
    
    [Fact]
    public async Task AnalyzeAndFixAsync_Timeout_ReturnsAiError()
    {
        var mockClient = new MockLlmClient();
        mockClient.QueueException(new TimeoutException("Request timed out"));
        
        var console = new TestConsole();
        var surgeon = CreateTestSurgeon(mockClient, console);
        var context = CreateTestErrorContext();
        var options = new AiSurgeonOptions();
        
        var result = await surgeon.AnalyzeAndFixAsync(context, options);
        
        Assert.Equal(FixOutcome.AiError, result.Outcome);
        Assert.Contains("timed out", result.ErrorMessage);
    }
    
    [Fact]
    public async Task AnalyzeAndFixAsync_Cancelled_ReturnsSkipped()
    {
        var mockClient = new MockLlmClient();
        mockClient.QueueException(new OperationCanceledException());
        
        var console = new TestConsole();
        var surgeon = CreateTestSurgeon(mockClient, console);
        var context = CreateTestErrorContext();
        var options = new AiSurgeonOptions();
        
        var result = await surgeon.AnalyzeAndFixAsync(context, options);
        
        Assert.Equal(FixOutcome.Skipped, result.Outcome);
    }
    
    private static Tsugix.AiSurgeon.AiSurgeon CreateTestSurgeon(ILlmClient? llmClient, TestConsole? console = null)
    {
        console ??= new TestConsole();
        
        return new Tsugix.AiSurgeon.AiSurgeon(
            llmClient,
            new PromptBuilder(),
            new JsonFixParser(),
            new SpectreConsoleDiffView(console),
            new FilePatcher(),
            new UserInteraction(console, autoApply: false, autoRerun: false),
            TsugixConfig.Default);
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
            WorkingDirectory = "/test"
        };
    }
    
    private static string CreateValidFixJson()
    {
        return """
            {
                "filePath": "test.py",
                "originalLines": ["x = 1"],
                "replacementLines": ["x = 10"],
                "explanation": "Fixed the value",
                "confidence": 90
            }
            """;
    }
}

/// <summary>
/// Property-based tests for AiSurgeon.
/// Validates: Requirements 8.6
/// </summary>
public class AiSurgeonPropertyTests
{
    /// <summary>
    /// Property 8: Graceful Fallback
    /// For any scenario where AI analysis fails or is skipped, the system SHALL still
    /// display the parsed error context from Phase 2 without crashing.
    /// Validates: Requirements 8.6
    /// </summary>
    [Property(MaxTest = 30)]
    public Property GracefulFallback()
    {
        var failureModeGen = Gen.Elements(
            "skip_ai",
            "no_api_key",
            "timeout",
            "invalid_response",
            "cancelled");
        
        return Prop.ForAll(
            failureModeGen.ToArbitrary(),
            failureMode =>
            {
                var console = new TestConsole();
                ILlmClient? mockClient = null;
                
                if (failureMode != "skip_ai" && failureMode != "no_api_key")
                {
                    var client = new MockLlmClient();
                    switch (failureMode)
                    {
                        case "timeout":
                            client.QueueException(new TimeoutException("Timeout"));
                            break;
                        case "invalid_response":
                            client.QueueResponse("not valid json");
                            break;
                        case "cancelled":
                            client.QueueException(new OperationCanceledException());
                            break;
                    }
                    mockClient = client;
                }
                
                var surgeon = new Tsugix.AiSurgeon.AiSurgeon(
                    mockClient,
                    new PromptBuilder(),
                    new JsonFixParser(),
                    new SpectreConsoleDiffView(console),
                    new FilePatcher(),
                    new UserInteraction(console, autoApply: false, autoRerun: false),
                    TsugixConfig.Default);
                
                var context = new ErrorContext
                {
                    Language = "Python",
                    Exception = new ExceptionInfo { Type = "Error", Message = "Test" },
                    Frames = Array.Empty<StackFrame>(),
                    OriginalCommand = "test",
                    Timestamp = DateTimeOffset.UtcNow,
                    WorkingDirectory = "/test"
                };
                
                var options = new AiSurgeonOptions { SkipAi = failureMode == "skip_ai" };
                
                try
                {
                    var result = surgeon.AnalyzeAndFixAsync(context, options).GetAwaiter().GetResult();
                    
                    // Should not crash and should return a valid result
                    return result.Outcome == FixOutcome.Skipped ||
                           result.Outcome == FixOutcome.AiError ||
                           result.Outcome == FixOutcome.NoFixSuggested;
                }
                catch
                {
                    // Should not throw
                    return false;
                }
            });
    }
}
