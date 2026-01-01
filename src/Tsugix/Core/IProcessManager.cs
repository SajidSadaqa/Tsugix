namespace Tsugix.Core;

/// <summary>
/// Manages the lifecycle of wrapped processes.
/// </summary>
public interface IProcessManager
{
    /// <summary>
    /// Executes a command as a subprocess and returns the result.
    /// </summary>
    /// <param name="command">The executable to run.</param>
    /// <param name="arguments">Arguments to pass to the executable.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The execution result containing exit code and captured output.</returns>
    Task<IExecutionResult> ExecuteAsync(
        string command,
        string[] arguments,
        CancellationToken cancellationToken = default);
}
