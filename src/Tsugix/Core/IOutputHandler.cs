namespace Tsugix.Core;

/// <summary>
/// Handles output streaming from wrapped processes.
/// </summary>
public interface IOutputHandler
{
    /// <summary>
    /// Handles a line of standard output from the wrapped process.
    /// </summary>
    /// <param name="line">The output line to handle.</param>
    void HandleStdout(string line);
    
    /// <summary>
    /// Handles a line of standard error from the wrapped process.
    /// </summary>
    /// <param name="line">The error line to handle.</param>
    void HandleStderr(string line);
    
    /// <summary>
    /// Gets all captured standard error content.
    /// </summary>
    /// <returns>The complete stderr content captured during execution.</returns>
    string GetCapturedStderr();
}
