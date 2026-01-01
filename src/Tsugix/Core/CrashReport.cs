namespace Tsugix.Core;

/// <summary>
/// Contains structured information about a process crash for downstream analysis.
/// </summary>
public sealed record CrashReport
{
    /// <summary>
    /// The captured standard error output containing the stack trace.
    /// </summary>
    public required string Stderr { get; init; }
    
    /// <summary>
    /// The exit code returned by the crashed process.
    /// </summary>
    public required int ExitCode { get; init; }
    
    /// <summary>
    /// The original command that was executed.
    /// </summary>
    public required string Command { get; init; }
    
    /// <summary>
    /// The timestamp when the crash occurred.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }
    
    /// <summary>
    /// The working directory where the command was executed.
    /// </summary>
    public required string WorkingDirectory { get; init; }
    
    /// <summary>
    /// Creates a CrashReport from an ExecutionResult.
    /// </summary>
    /// <param name="result">The execution result to convert.</param>
    /// <param name="workingDirectory">The working directory where the command ran.</param>
    /// <returns>A new CrashReport instance.</returns>
    public static CrashReport FromExecutionResult(IExecutionResult result, string workingDirectory)
    {
        return new CrashReport
        {
            Stderr = result.StandardError,
            ExitCode = result.ExitCode,
            Command = result.Command,
            Timestamp = result.Timestamp,
            WorkingDirectory = workingDirectory
        };
    }
}
