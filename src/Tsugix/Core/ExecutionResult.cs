namespace Tsugix.Core;

/// <summary>
/// Represents the result of executing a wrapped process.
/// </summary>
public sealed record ExecutionResult : IExecutionResult
{
    /// <inheritdoc />
    public required int ExitCode { get; init; }
    
    /// <inheritdoc />
    public required string StandardError { get; init; }
    
    /// <inheritdoc />
    public required string Command { get; init; }
    
    /// <inheritdoc />
    public required DateTimeOffset Timestamp { get; init; }
    
    /// <inheritdoc />
    public bool IsSuccess => ExitCode == 0;
}
