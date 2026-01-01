using Tsugix.Core;
using Tsugix.Process;
using System.Runtime.InteropServices;
using Xunit;

namespace Tsugix.Tests.Integration;

/// <summary>
/// Integration tests for end-to-end process execution.
/// Feature: tsugix-phase1-universal-wrapper
/// Validates: Requirements 3.2, 5.1, 5.3
/// </summary>
public class EndToEndTests
{
    private static (string Executable, string[] Arguments) GetShellCommand(string shellCommand)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ("cmd", new[] { "/c", shellCommand });
        }

        return ("bash", new[] { "-lc", shellCommand });
    }

    /// <summary>
    /// Test: Successful command execution returns exit code 0.
    /// </summary>
    [Fact]
    public async Task SuccessfulCommand_ReturnsExitCodeZero()
    {
        var outputHandler = new OutputHandler(TextWriter.Null, TextWriter.Null);
        var processManager = new ProcessManager(outputHandler);
        
        var (executable, arguments) = GetShellCommand("echo test");
        var result = await processManager.ExecuteAsync(executable, arguments);
        
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
        
        var (executable, arguments) = GetShellCommand("exit 1");
        var result = await processManager.ExecuteAsync(executable, arguments);
        
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
        
        var (executable, arguments) = GetShellCommand("echo hello");
        await processManager.ExecuteAsync(executable, arguments);
        
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
        
        var (executable, arguments) = GetShellCommand("echo error 1>&2");
        await processManager.ExecuteAsync(executable, arguments);
        
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
        
        var (executable, arguments) = GetShellCommand("exit 42");
        var result = await processManager.ExecuteAsync(executable, arguments);
        
        Assert.False(result.IsSuccess);
        
        var crashReport = CrashReport.FromExecutionResult(result, Environment.CurrentDirectory);
        
        Assert.Equal(42, crashReport.ExitCode);
        Assert.Contains(executable, crashReport.Command);
        Assert.NotNull(crashReport.WorkingDirectory);
        Assert.NotEqual(default, crashReport.Timestamp);
    }
}
