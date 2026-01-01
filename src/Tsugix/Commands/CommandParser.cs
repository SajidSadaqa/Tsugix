namespace Tsugix.Commands;

/// <summary>
/// Parses command-line arguments for the run command.
/// Extracted for testability.
/// </summary>
public static class CommandParser
{
    /// <summary>
    /// Parses command arguments into executable and arguments.
    /// </summary>
    /// <param name="commandArgs">The raw command arguments.</param>
    /// <returns>A tuple of (executable, arguments) or null if invalid.</returns>
    public static (string Executable, string[] Arguments)? Parse(string[] commandArgs)
    {
        if (commandArgs == null || commandArgs.Length == 0)
        {
            return null;
        }
        
        var executable = commandArgs[0];
        var arguments = commandArgs.Length > 1 
            ? commandArgs[1..] 
            : Array.Empty<string>();
        
        return (executable, arguments);
    }
    
    /// <summary>
    /// Reconstructs the full command string from parsed components.
    /// </summary>
    public static string Reconstruct(string executable, string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return executable;
        }
        return $"{executable} {string.Join(" ", arguments)}";
    }
}
