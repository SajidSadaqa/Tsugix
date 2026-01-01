using FsCheck;
using FsCheck.Xunit;
using Tsugix.Commands;
using Xunit;

namespace Tsugix.Tests.Commands;

/// <summary>
/// Property-based tests for command argument parsing.
/// Feature: tsugix-phase1-universal-wrapper, Property 1: Argument Parsing Preserves All Arguments
/// Validates: Requirements 1.1, 1.2
/// </summary>
public class CommandParserTests
{
    /// <summary>
    /// Property 1: Argument Parsing Preserves All Arguments
    /// For any sequence of command-line arguments, the parser SHALL extract and preserve
    /// all arguments in their original order without modification or loss.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ArgumentParsingPreservesAllArguments()
    {
        return Prop.ForAll(
            Arb.From<NonEmptyString>(),
            Arb.From<string[]>(),
            (executable, args) =>
            {
                // Filter out null/empty args for valid test cases
                var validArgs = args?.Where(a => !string.IsNullOrEmpty(a)).ToArray() 
                    ?? Array.Empty<string>();
                
                var execStr = executable.Get;
                if (string.IsNullOrWhiteSpace(execStr))
                    return true; // Skip invalid inputs
                
                // Build input array
                var input = new[] { execStr }.Concat(validArgs).ToArray();
                
                // Parse
                var result = CommandParser.Parse(input);
                
                // Verify
                if (result == null)
                    return false;
                
                var (parsedExec, parsedArgs) = result.Value;
                
                // Executable must match
                if (parsedExec != execStr)
                    return false;
                
                // Arguments must match in count and order
                if (parsedArgs.Length != validArgs.Length)
                    return false;
                
                for (int i = 0; i < validArgs.Length; i++)
                {
                    if (parsedArgs[i] != validArgs[i])
                        return false;
                }
                
                return true;
            });
    }
    
    /// <summary>
    /// Property: Empty input returns null.
    /// </summary>
    [Fact]
    public void EmptyInput_ReturnsNull()
    {
        var result = CommandParser.Parse(Array.Empty<string>());
        Assert.Null(result);
    }
    
    /// <summary>
    /// Property: Null input returns null.
    /// </summary>
    [Fact]
    public void NullInput_ReturnsNull()
    {
        var result = CommandParser.Parse(null!);
        Assert.Null(result);
    }
    
    /// <summary>
    /// Property: Single argument becomes executable with no args.
    /// </summary>
    [Fact]
    public void SingleArgument_BecomesExecutableOnly()
    {
        var result = CommandParser.Parse(new[] { "python" });
        
        Assert.NotNull(result);
        Assert.Equal("python", result.Value.Executable);
        Assert.Empty(result.Value.Arguments);
    }
    
    /// <summary>
    /// Property: Multiple arguments are correctly split.
    /// </summary>
    [Fact]
    public void MultipleArguments_CorrectlySplit()
    {
        var result = CommandParser.Parse(new[] { "python", "main.py", "--verbose" });
        
        Assert.NotNull(result);
        Assert.Equal("python", result.Value.Executable);
        Assert.Equal(2, result.Value.Arguments.Length);
        Assert.Equal("main.py", result.Value.Arguments[0]);
        Assert.Equal("--verbose", result.Value.Arguments[1]);
    }
}
