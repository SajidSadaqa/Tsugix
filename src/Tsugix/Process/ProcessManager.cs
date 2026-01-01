using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Tsugix.Core;
using Tsugix.Logging;
using Tsugix.Telemetry;

namespace Tsugix.Process;

/// <summary>
/// Manages the lifecycle of wrapped processes using CliWrap.
/// Handles graceful shutdown and child process tree termination.
/// </summary>
public class ProcessManager : IProcessManager
{
    private readonly IOutputHandler _outputHandler;
    private readonly ILogger<ProcessManager> _logger;
    private System.Diagnostics.Process? _currentProcess;
    private readonly object _processLock = new();
    
    public ProcessManager() : this(new OutputHandler())
    {
    }
    
    public ProcessManager(IOutputHandler outputHandler)
    {
        _outputHandler = outputHandler;
        _logger = TsugixLogger.CreateLogger<ProcessManager>();
    }
    
    /// <inheritdoc />
    public async Task<IExecutionResult> ExecuteAsync(
        string command,
        string[] arguments,
        CancellationToken cancellationToken = default)
    {
        var fullCommand = arguments.Length > 0
            ? $"{command} {string.Join(" ", arguments)}"
            : command;
        
        _logger.LogDebug(LogEvents.ProcessStarted, "Starting process: {Command}", fullCommand);
        using var metricsScope = TsugixMetrics.Instance.StartCommand(fullCommand);
        
        try
        {
            // Register cancellation callback for graceful shutdown
            using var registration = cancellationToken.Register(() => TerminateCurrentProcess());
            
            var cmd = Cli.Wrap(command)
                .WithArguments(arguments)
                .WithWorkingDirectory(Environment.CurrentDirectory)
                .WithEnvironmentVariables(env => 
                {
                    foreach (var kvp in Environment.GetEnvironmentVariables())
                    {
                        if (kvp is System.Collections.DictionaryEntry entry &&
                            entry.Key is string key &&
                            entry.Value is string value)
                        {
                            env.Set(key, value);
                        }
                    }
                })
                .WithStandardOutputPipe(PipeTarget.ToDelegate(_outputHandler.HandleStdout))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(_outputHandler.HandleStderr))
                .WithValidation(CommandResultValidation.None); // Don't throw on non-zero exit
            
            var result = await cmd.ExecuteAsync(cancellationToken);
            
            var success = result.ExitCode == 0;
            metricsScope.SetSuccess(success);
            
            _logger.LogDebug(LogEvents.ProcessCompleted, 
                "Process completed: {Command} with exit code {ExitCode}", 
                fullCommand, result.ExitCode);
            
            return new ExecutionResult
            {
                ExitCode = result.ExitCode,
                StandardError = _outputHandler.GetCapturedStderr(),
                Command = fullCommand,
                Timestamp = DateTimeOffset.UtcNow
            };
        }
        catch (OperationCanceledException)
        {
            // Ensure cleanup on cancellation
            TerminateCurrentProcess();
            
            _logger.LogInformation(LogEvents.ProcessCancelled, "Process cancelled: {Command}", fullCommand);
            metricsScope.SetSuccess(false);
            
            return new ExecutionResult
            {
                ExitCode = 130, // Unix convention for SIGINT
                StandardError = "Process terminated by user",
                Command = fullCommand,
                Timestamp = DateTimeOffset.UtcNow
            };
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Command not found or access denied
            _logger.LogWarning(LogEvents.ProcessNotFound, "Command not found: {Command}", command);
            metricsScope.SetSuccess(false);
            
            Console.Error.WriteLine($"tsugix: error: Command not found: {command}");
            return new ExecutionResult
            {
                ExitCode = 127, // Unix convention for command not found
                StandardError = $"Command not found: {command}",
                Command = fullCommand,
                Timestamp = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex) when (ex.Message.Contains("not find") || ex.Message.Contains("not found"))
        {
            // Fallback for other "not found" scenarios
            _logger.LogWarning(LogEvents.ProcessNotFound, "Command not found: {Command}", command);
            metricsScope.SetSuccess(false);
            
            Console.Error.WriteLine($"tsugix: error: Command not found: {command}");
            return new ExecutionResult
            {
                ExitCode = 127,
                StandardError = $"Command not found: {command}",
                Command = fullCommand,
                Timestamp = DateTimeOffset.UtcNow
            };
        }
    }
    
    /// <summary>
    /// Terminates the current process and its child process tree.
    /// </summary>
    private void TerminateCurrentProcess()
    {
        lock (_processLock)
        {
            if (_currentProcess == null || _currentProcess.HasExited)
                return;
            
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // On Windows, use taskkill to terminate the process tree
                    TerminateProcessTreeWindows(_currentProcess.Id);
                }
                else
                {
                    // On Unix, send SIGTERM to the process group
                    TerminateProcessTreeUnix(_currentProcess.Id);
                }
            }
            catch
            {
                // Best effort - ignore errors during cleanup
            }
            finally
            {
                _currentProcess = null;
            }
        }
    }
    
    /// <summary>
    /// Terminates a process tree on Windows using taskkill.
    /// </summary>
    private static void TerminateProcessTreeWindows(int processId)
    {
        try
        {
            using var taskkill = System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/T /F /PID {processId}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            
            taskkill?.WaitForExit(5000);
        }
        catch
        {
            // Fallback: try to kill just the main process
            try
            {
                var process = System.Diagnostics.Process.GetProcessById(processId);
                process.Kill();
            }
            catch
            {
                // Process may have already exited
            }
        }
    }
    
    /// <summary>
    /// Terminates a process tree on Unix by sending SIGTERM to the process group.
    /// </summary>
    private static void TerminateProcessTreeUnix(int processId)
    {
        try
        {
            // First try SIGTERM for graceful shutdown
            using var kill = System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = "kill",
                Arguments = $"-TERM -{processId}", // Negative PID = process group
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            
            kill?.WaitForExit(1000);
            
            // Give processes time to clean up
            Thread.Sleep(500);
            
            // Check if still running and force kill if needed
            try
            {
                var process = System.Diagnostics.Process.GetProcessById(processId);
                if (!process.HasExited)
                {
                    using var killForce = System.Diagnostics.Process.Start(new ProcessStartInfo
                    {
                        FileName = "kill",
                        Arguments = $"-KILL -{processId}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    });
                    
                    killForce?.WaitForExit(1000);
                }
            }
            catch
            {
                // Process already exited
            }
        }
        catch
        {
            // Fallback: try to kill just the main process
            try
            {
                var process = System.Diagnostics.Process.GetProcessById(processId);
                process.Kill();
            }
            catch
            {
                // Process may have already exited
            }
        }
    }
}
