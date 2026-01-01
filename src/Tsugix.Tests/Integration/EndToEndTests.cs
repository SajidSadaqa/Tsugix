using Tsugix.Core;
using Tsugix.Process;
using Xunit;

namespace Tsugix.Tests.Integration;

/// <summary>
/// Integration tests for end-to-end process execution.
/// Feature: tsugix-phase1-universal-wrapper
/// Validates: Requirements 3.2, 5.1, 5.3
/// </summary>
public class EndToEndTests
{
    /// <summary>
    /// Test: Successful command execution returns exit code 0.
    /// </summary>
    [Fact]
    public async Task SuccessfulCommand_ReturnsExitCodeZero()
    {
        var outputHandler = new OutputHandler(TextWriter.Null, TextWriter.Null);
        var processManager = new ProcessManager(outputHandler);
        
        // Use 'cmd /c echo test' on Windows
        var result = await processManager.ExecuteAsync("cmd", new[] { "/c", "echo", "test" });
        
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.IsSuccess);
    }
    
    /// <summary>
    /// Test: Failed command returns non-zero exit code.
    /// </summary>
    [Fact]
    public async Task FailedCommand_ReturnsNonZeroExitCode()
    {
        var outputHandler = new OutputHandler(TextWriter.Null, TextWriter.Null);
        var processManager = new ProcessManager(outputHandler);
        
        // Use 'cmd /c exit 1' to simulate failure
        var result = await processManager.ExecuteAsync("cmd", new[] { "/c", "exit", "1" });
        
        Assert.Equal(1, result.ExitCode);
        Assert.False(result.IsSuccess);
    }
    
    /// <summary>
    /// Test: Stdout is captured correctly.
    /// </summary>
    [Fact]
    public async Task StdoutIsCaptured()
    {
        var stdoutWriter = new StringWriter();
        var outputHandler = new OutputHandler(stdoutWriter, TextWriter.Null);
        var processManager = new ProcessManager(outputHandler);
        
        await processManager.ExecuteAsync("cmd", new[] { "/c", "echo", "hello" });
        
        var output = stdoutWriter.ToString();
        Assert.Contains("hello", output);
    }
    
    /// <summary>
    /// Test: Stderr is captured for crash report.
    /// </summary>
    [Fact]
    public async Task StderrIsCapturedForCrashReport()
    {
        var stderrWriter = new StringWriter();
        var outputHandler = new OutputHandler(TextWriter.Null, stderrWriter);
        var processManager = new ProcessManager(outputHandler);
        
        // Use 'cmd /c echo error 1>&2' to write to stderr
        await processManager.ExecuteAsync("cmd", new[] { "/c", "echo", "error", "1>&2" });
        
        var captured = outputHandler.GetCapturedStderr();
        Assert.Contains("error", captured);
    }
    
    /// <summary>
    /// Test: CrashReport is created for failed process.
    /// </summary>
    [Fact]
    public async Task CrashReport_CreatedForFailedProcess()
    {
        var outputHandler = new OutputHandler(TextWriter.Null, TextWriter.Null);
        var processManager = new ProcessManager(outputHandler);
        
        var result = await processManager.ExecuteAsync("cmd", new[] { "/c", "exit", "42" });
        
        Assert.False(result.IsSuccess);
        
        var crashReport = CrashReport.FromExecutionResult(result, Environment.CurrentDirectory);
        
        Assert.Equal(42, crashReport.ExitCode);
        Assert.Contains("cmd", crashReport.Command);
        Assert.NotNull(crashReport.WorkingDirectory);
        Assert.NotEqual(default, crashReport.Timestamp);
    }
}
