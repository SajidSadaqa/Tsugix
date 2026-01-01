namespace Tsugix.Core;

/// <summary>
/// Represents the result of executing a wrapped process.
/// </summary>
public interface IExecutionResult
{
    /// <summary>
    /// The exit code returned by the wrapped process.
    /// </summary>
    int ExitCode { get; }
    
    /// <summary>
    /// Whether the process completed successfully (exit code 0).
    /// </summary>
    bool IsSuccess => ExitCode == 0;
    
    /// <summary>
    /// The captured standard error output from the process.
    /// </summary>
    string StandardError { get; }
    
    /// <summary>
    /// The original command that was executed.
    /// </summary>
    string Command { get; }
    
    /// <summary>
    /// The timestamp when the process completed.
    /// </summary>
    DateTimeOffset Timestamp { get; }
}
