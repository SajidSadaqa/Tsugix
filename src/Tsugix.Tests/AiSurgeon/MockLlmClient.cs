using Tsugix.AiSurgeon;

namespace Tsugix.Tests.AiSurgeon;

/// <summary>
/// Mock LLM client for testing purposes.
/// </summary>
public class MockLlmClient : ILlmClient
{
    private readonly Queue<Func<string, string, LlmOptions, CancellationToken, Task<string>>> _responses = new();
    private readonly List<(string SystemPrompt, string UserPrompt, LlmOptions Options)> _calls = new();
    
    /// <summary>
    /// Gets the recorded calls made to this client.
    /// </summary>
    public IReadOnlyList<(string SystemPrompt, string UserPrompt, LlmOptions Options)> Calls => _calls;
    
    /// <summary>
    /// Queues a response to be returned on the next call.
    /// </summary>
    public void QueueResponse(string response)
    {
        _responses.Enqueue((_, _, _, _) => Task.FromResult(response));
    }
    
    /// <summary>
    /// Queues a response function to be called on the next call.
    /// </summary>
    public void QueueResponse(Func<string, string, LlmOptions, CancellationToken, Task<string>> responseFunc)
    {
        _responses.Enqueue(responseFunc);
    }
    
    /// <summary>
    /// Queues an exception to be thrown on the next call.
    /// </summary>
    public void QueueException(Exception exception)
    {
        _responses.Enqueue((_, _, _, _) => throw exception);
    }
    
    /// <summary>
    /// Queues a timeout (OperationCanceledException) on the next call.
    /// </summary>
    public void QueueTimeout()
    {
        _responses.Enqueue((_, _, _, _) => throw new OperationCanceledException("Simulated timeout"));
    }
    
    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        LlmOptions options,
        CancellationToken cancellationToken = default)
    {
        _calls.Add((systemPrompt, userPrompt, options));
        
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No response queued in MockLlmClient");
        }
        
        var responseFunc = _responses.Dequeue();
        return await responseFunc(systemPrompt, userPrompt, options, cancellationToken);
    }
    
    /// <summary>
    /// Clears all queued responses and recorded calls.
    /// </summary>
    public void Reset()
    {
        _responses.Clear();
        _calls.Clear();
    }
}
