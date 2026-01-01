using FsCheck;
using FsCheck.Xunit;
using Tsugix.Core;
using Xunit;

namespace Tsugix.Tests.Core;

/// <summary>
/// Property-based tests for CrashReport.
/// Feature: tsugix-phase1-universal-wrapper, Property 5: CrashReport Completeness
/// Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5
/// </summary>
public class CrashReportTests
{
    /// <summary>
    /// Property 5: CrashReport Completeness
    /// For any Wrapped_Process that exits with a non-zero exit code, the generated CrashReport
    /// SHALL contain: (a) the complete stderr content, (b) the exact exit code, (c) the original
    /// command string, and (d) a valid timestamp.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CrashReportCompleteness()
    {
        return Prop.ForAll(
            Arb.From<NonEmptyString>(), // stderr
            Gen.Choose(1, 255).ToArbitrary(), // non-zero exit code
            Arb.From<NonEmptyString>(), // command
            (stderr, exitCode, command) =>
            {
                var stderrStr = stderr.Get;
                var commandStr = command.Get;
                var workingDirStr = "/test/dir";
                
                var executionResult = new ExecutionResult
                {
                    ExitCode = exitCode,
                    StandardError = stderrStr,
                    Command = commandStr,
                    Timestamp = DateTimeOffset.UtcNow
                };
                
                var crashReport = CrashReport.FromExecutionResult(executionResult, workingDirStr);
                
                // Verify all fields are populated correctly
                return crashReport.Stderr == stderrStr &&
                       crashReport.ExitCode == exitCode &&
                       crashReport.Command == commandStr &&
                       crashReport.WorkingDirectory == workingDirStr &&
                       crashReport.Timestamp != default;
            });
    }
    
    /// <summary>
    /// Property: CrashReport preserves stderr content exactly.
    /// </summary>
    [Fact]
    public void CrashReport_PreservesStderr()
    {
        var stderr = "Traceback (most recent call last):\n  File \"main.py\", line 1\nNameError: name 'x' is not defined";
        
        var result = new ExecutionResult
        {
            ExitCode = 1,
            StandardError = stderr,
            Command = "python main.py",
            Timestamp = DateTimeOffset.UtcNow
        };
        
        var crashReport = CrashReport.FromExecutionResult(result, "/home/user/project");
        
        Assert.Equal(stderr, crashReport.Stderr);
    }
    
    /// <summary>
    /// Property: CrashReport preserves exit code exactly.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(127)]
    [InlineData(255)]
    public void CrashReport_PreservesExitCode(int exitCode)
    {
        var result = new ExecutionResult
        {
            ExitCode = exitCode,
            StandardError = "error",
            Command = "test",
            Timestamp = DateTimeOffset.UtcNow
        };
        
        var crashReport = CrashReport.FromExecutionResult(result, "/tmp");
        
        Assert.Equal(exitCode, crashReport.ExitCode);
    }
    
    /// <summary>
    /// Property: CrashReport has valid timestamp.
    /// </summary>
    [Fact]
    public void CrashReport_HasValidTimestamp()
    {
        var before = DateTimeOffset.UtcNow;
        
        var result = new ExecutionResult
        {
            ExitCode = 1,
            StandardError = "error",
            Command = "test",
            Timestamp = DateTimeOffset.UtcNow
        };
        
        var crashReport = CrashReport.FromExecutionResult(result, "/tmp");
        
        var after = DateTimeOffset.UtcNow;
        
        Assert.True(crashReport.Timestamp >= before);
        Assert.True(crashReport.Timestamp <= after);
    }
}
