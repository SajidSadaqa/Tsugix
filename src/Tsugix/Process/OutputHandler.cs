using System.Text;
using Tsugix.Core;

namespace Tsugix.Process;

/// <summary>
/// Handles output streaming from wrapped processes.
/// Streams to console in real-time while buffering stderr for crash analysis.
/// </summary>
public class OutputHandler : IOutputHandler
{
    private readonly StringBuilder _stderrBuffer = new();
    private readonly TextWriter _consoleOut;
    private readonly TextWriter _consoleErr;
    private readonly object _lock = new();
    
    public OutputHandler() : this(Console.Out, Console.Error)
    {
    }
    
    public OutputHandler(TextWriter consoleOut, TextWriter consoleErr)
    {
        _consoleOut = consoleOut;
        _consoleErr = consoleErr;
    }
    
    /// <inheritdoc />
    public void HandleStdout(string line)
    {
        // Stream to console in real-time, preserving ANSI codes
        _consoleOut.WriteLine(line);
    }
    
    /// <inheritdoc />
    public void HandleStderr(string line)
    {
        // Stream to console in real-time
        _consoleErr.WriteLine(line);
        
        // Also buffer for crash analysis
        lock (_lock)
        {
            _stderrBuffer.AppendLine(line);
        }
    }
    
    /// <inheritdoc />
    public string GetCapturedStderr()
    {
        lock (_lock)
        {
            return _stderrBuffer.ToString();
        }
    }
}
