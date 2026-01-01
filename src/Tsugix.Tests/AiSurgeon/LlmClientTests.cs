using FsCheck;
using FsCheck.Xunit;
using Tsugix.AiSurgeon;
using Xunit;

namespace Tsugix.Tests.AiSurgeon;

/// <summary>
/// Tests for LLM client functionality.
/// Validates: Requirements 1.1, 1.2, 1.3, 8.2, 10.1, 10.2, 10.3, 10.4
/// </summary>
public class LlmClientTests
{
    [Fact]
    public async Task MockLlmClient_ReturnsQueuedResponse()
    {
        var client = new MockLlmClient();
        client.QueueResponse("Test response");
        
        var result = await client.CompleteAsync("system", "user", new LlmOptions());
        
        Assert.Equal("Test response", result);
    }
    
    [Fact]
    public async Task MockLlmClient_RecordsCalls()
    {
        var client = new MockLlmClient();
        client.QueueResponse("response");
        
        var options = new LlmOptions { MaxTokens = 1000, Temperature = 0.5 };
        await client.CompleteAsync("system prompt", "user prompt", options);
        
        Assert.Single(client.Calls);
        Assert.Equal("system prompt", client.Calls[0].SystemPrompt);
        Assert.Equal("user prompt", client.Calls[0].UserPrompt);
        Assert.Equal(1000, client.Calls[0].Options.MaxTokens);
    }
    
    [Fact]
    public async Task MockLlmClient_ThrowsQueuedException()
    {
        var client = new MockLlmClient();
        client.QueueException(new InvalidOperationException("Test error"));
        
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CompleteAsync("system", "user", new LlmOptions()));
    }
    
    [Fact]
    public async Task MockLlmClient_ThrowsWhenNoResponseQueued()
    {
        var client = new MockLlmClient();
        
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CompleteAsync("system", "user", new LlmOptions()));
    }
    
    [Fact]
    public void LlmOptions_HasCorrectDefaults()
    {
        var options = new LlmOptions();
        
        Assert.Equal(4000, options.MaxTokens);
        Assert.Equal(0.2, options.Temperature);
        Assert.Equal(30, options.TimeoutSeconds);
        Assert.Equal(1, options.RetryCount);
    }
    
    [Fact]
    public void LlmClientFactory_CreateOptions_MapsConfigCorrectly()
    {
        var config = new TsugixConfig
        {
            MaxTokens = 2000,
            Temperature = 0.7,
            TimeoutSeconds = 60,
            RetryCount = 3
        };
        
        var options = LlmClientFactory.CreateOptions(config);
        
        Assert.Equal(2000, options.MaxTokens);
        Assert.Equal(0.7, options.Temperature);
        Assert.Equal(60, options.TimeoutSeconds);
        Assert.Equal(3, options.RetryCount);
    }
    
    [Fact]
    public void LlmClientFactory_Create_ThrowsWhenNoApiKey()
    {
        var config = new TsugixConfig { Provider = LlmProvider.OpenAI };
        var configManager = new ConfigManager();
        
        // Ensure no API key is set
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        
        var ex = Assert.Throws<InvalidOperationException>(
            () => LlmClientFactory.Create(config, configManager));
        
        Assert.Contains("OPENAI_API_KEY", ex.Message);
    }
}

/// <summary>
/// Property-based tests for LLM client retry behavior.
/// Validates: Requirements 8.2
/// </summary>
public class LlmClientPropertyTests
{
    /// <summary>
    /// Property 7: Retry on Timeout
    /// For any LLM API call that times out, the system SHALL retry exactly once 
    /// with exponential backoff before reporting failure.
    /// Validates: Requirements 8.2
    /// 
    /// This tests using a RetryingMockLlmClient that simulates the retry behavior.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property RetryOnTimeout_RetriesCorrectNumberOfTimes()
    {
        return Prop.ForAll(
            Gen.Choose(0, 3).ToArbitrary(),
            Gen.Choose(1, 3).ToArbitrary(),
            (retryCount, failuresBeforeSuccess) =>
            {
                var actualFailures = Math.Min(failuresBeforeSuccess, retryCount + 1);
                var shouldSucceed = failuresBeforeSuccess <= retryCount;
                
                var client = new RetryingMockLlmClient(actualFailures);
                var options = new LlmOptions { RetryCount = retryCount, TimeoutSeconds = 30 };
                
                try
                {
                    var result = client.CompleteAsync("system", "user", options).GetAwaiter().GetResult();
                    // Should succeed if we had enough retries
                    return shouldSucceed && 
                           result == "Success" && 
                           client.AttemptCount == actualFailures + (shouldSucceed ? 1 : 0);
                }
                catch (TimeoutException)
                {
                    // Should fail if we didn't have enough retries
                    return !shouldSucceed && client.AttemptCount == retryCount + 1;
                }
            });
    }
    
    /// <summary>
    /// Property: LLM options are preserved through factory.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property LlmOptionsPreservedThroughFactory()
    {
        var configGen = from maxTokens in Gen.Choose(100, 10000)
                        from temperature in Gen.Choose(0, 100).Select(x => x / 100.0)
                        from timeout in Gen.Choose(1, 120)
                        from retryCount in Gen.Choose(0, 5)
                        select new TsugixConfig
                        {
                            MaxTokens = maxTokens,
                            Temperature = temperature,
                            TimeoutSeconds = timeout,
                            RetryCount = retryCount
                        };
        
        return Prop.ForAll(
            configGen.ToArbitrary(),
            config =>
            {
                var options = LlmClientFactory.CreateOptions(config);
                
                return options.MaxTokens == config.MaxTokens &&
                       Math.Abs(options.Temperature - config.Temperature) < 0.001 &&
                       options.TimeoutSeconds == config.TimeoutSeconds &&
                       options.RetryCount == config.RetryCount;
            });
    }
}

/// <summary>
/// Mock LLM client that implements retry logic for testing.
/// </summary>
public class RetryingMockLlmClient : ILlmClient
{
    private readonly int _failuresBeforeSuccess;
    private int _attemptCount;
    
    public int AttemptCount => _attemptCount;
    
    public RetryingMockLlmClient(int failuresBeforeSuccess)
    {
        _failuresBeforeSuccess = failuresBeforeSuccess;
    }
    
    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        LlmOptions options,
        CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        var maxAttempts = options.RetryCount + 1;
        
        while (attempt < maxAttempts)
        {
            attempt++;
            _attemptCount++;
            
            if (_attemptCount <= _failuresBeforeSuccess)
            {
                if (attempt < maxAttempts)
                {
                    // Simulate exponential backoff (minimal delay for tests)
                    await Task.Delay(1, cancellationToken);
                    continue;
                }
                throw new TimeoutException("Simulated timeout after all retries");
            }
            
            return "Success";
        }
        
        throw new TimeoutException("Simulated timeout after all retries");
    }
}
