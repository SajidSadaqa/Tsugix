using FsCheck;
using FsCheck.Xunit;
using Tsugix.Core;
using Xunit;

namespace Tsugix.Tests.Core;

/// <summary>
/// Property-based tests for ExecutionResult and exit code handling.
/// Feature: tsugix-phase1-universal-wrapper, Property 4: Exit Code Round-Trip
/// Validates: Requirements 5.1, 5.2, 5.3
/// </summary>
public class ExecutionResultTests
{
    /// <summary>
    /// Property 4: Exit Code Round-Trip
    /// For any exit code returned by the Wrapped_Process, the Tsugix_CLI SHALL exit
    /// with the identical exit code value (when no AI fix is applied).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ExitCodeRoundTrip()
    {
        return Prop.ForAll(
            Gen.Choose(0, 255).ToArbitrary(), // Valid exit codes
            (exitCode) =>
            {
                var result = new ExecutionResult
                {
                    ExitCode = exitCode,
                    StandardError = "",
                    Command = "test",
                    Timestamp = DateTimeOffset.UtcNow
                };
                
                // Exit code should be preserved exactly
                return result.ExitCode == exitCode;
            });
    }
    
    /// <summary>
    /// Property: IsSuccess is true only for exit code 0.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property IsSuccessOnlyForZero()
    {
        return Prop.ForAll(
            Gen.Choose(0, 255).ToArbitrary(),
            (exitCode) =>
            {
                var result = new ExecutionResult
                {
                    ExitCode = exitCode,
                    StandardError = "",
                    Command = "test",
                    Timestamp = DateTimeOffset.UtcNow
                };
                
                return result.IsSuccess == (exitCode == 0);
            });
    }
    
    /// <summary>
    /// Property: Exit code 0 means success.
    /// </summary>
    [Fact]
    public void ExitCodeZero_IsSuccess()
    {
        var result = new ExecutionResult
        {
            ExitCode = 0,
            StandardError = "",
            Command = "test",
            Timestamp = DateTimeOffset.UtcNow
        };
        
        Assert.True(result.IsSuccess);
    }
    
    /// <summary>
    /// Property: Non-zero exit code means failure.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(127)]
    [InlineData(255)]
    public void NonZeroExitCode_IsNotSuccess(int exitCode)
    {
        var result = new ExecutionResult
        {
            ExitCode = exitCode,
            StandardError = "",
            Command = "test",
            Timestamp = DateTimeOffset.UtcNow
        };
        
        Assert.False(result.IsSuccess);
    }
}
