using FsCheck;
using FsCheck.Xunit;
using Tsugix.Process;
using Xunit;

namespace Tsugix.Tests.Process;

/// <summary>
/// Property-based tests for OutputHandler.
/// Feature: tsugix-phase1-universal-wrapper, Property 3: Output Passthrough Integrity
/// Validates: Requirements 3.1, 3.2, 3.3, 4.1, 4.2
/// </summary>
public class OutputHandlerTests
{
    /// <summary>
    /// Property 3: Output Passthrough Integrity
    /// For any output emitted by the Wrapped_Process (stdout or stderr), the OutputHandler
    /// SHALL pass through the exact byte sequence without adding, removing, or modifying
    /// any characters including ANSI escape codes.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property OutputPassthroughIntegrity_Stdout()
    {
        return Prop.ForAll(
            Arb.From<NonEmptyString>(),
            (input) =>
            {
                var inputStr = input.Get;
                var capturedOutput = new StringWriter();
                var handler = new OutputHandler(capturedOutput, TextWriter.Null);
                
                handler.HandleStdout(inputStr);
                
                var output = capturedOutput.ToString();
                // Output should contain the exact input (plus newline from WriteLine)
                return output == inputStr + Environment.NewLine;
            });
    }
    
    /// <summary>
    /// Property 3: Output Passthrough Integrity for stderr
    /// </summary>
    [Property(MaxTest = 100)]
    public Property OutputPassthroughIntegrity_Stderr()
    {
        return Prop.ForAll(
            Arb.From<NonEmptyString>(),
            (input) =>
            {
                var inputStr = input.Get;
                var capturedError = new StringWriter();
                var handler = new OutputHandler(TextWriter.Null, capturedError);
                
                handler.HandleStderr(inputStr);
                
                var output = capturedError.ToString();
                // Output should contain the exact input (plus newline from WriteLine)
                return output == inputStr + Environment.NewLine;
            });
    }
    
    /// <summary>
    /// Property: ANSI escape codes are preserved.
    /// </summary>
    [Fact]
    public void AnsiEscapeCodes_ArePreserved()
    {
        var ansiRed = "\u001b[31m";
        var ansiReset = "\u001b[0m";
        var input = $"{ansiRed}Error message{ansiReset}";
        
        var capturedOutput = new StringWriter();
        var handler = new OutputHandler(capturedOutput, TextWriter.Null);
        
        handler.HandleStdout(input);
        
        var output = capturedOutput.ToString();
        Assert.Contains(ansiRed, output);
        Assert.Contains(ansiReset, output);
    }
    
    /// <summary>
    /// Property: Stderr is captured for later retrieval.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property StderrIsCaptured()
    {
        return Prop.ForAll(
            Arb.From<NonEmptyString>(),
            (input) =>
            {
                var inputStr = input.Get;
                var handler = new OutputHandler(TextWriter.Null, TextWriter.Null);
                
                handler.HandleStderr(inputStr);
                
                var captured = handler.GetCapturedStderr();
                // Captured stderr should contain the input
                return captured.Contains(inputStr);
            });
    }
    
    /// <summary>
    /// Property: Multiple stderr lines are all captured.
    /// </summary>
    [Fact]
    public void MultipleStderrLines_AllCaptured()
    {
        var handler = new OutputHandler(TextWriter.Null, TextWriter.Null);
        
        handler.HandleStderr("Line 1");
        handler.HandleStderr("Line 2");
        handler.HandleStderr("Line 3");
        
        var captured = handler.GetCapturedStderr();
        Assert.Contains("Line 1", captured);
        Assert.Contains("Line 2", captured);
        Assert.Contains("Line 3", captured);
    }
}
