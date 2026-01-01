using System.Collections.Concurrent;

namespace Tsugix.AiSurgeon;

/// <summary>
/// Token bucket rate limiter for LLM API calls.
/// Prevents hitting API rate limits by proactively throttling requests.
/// </summary>
public sealed class RateLimiter
{
    private readonly int _maxTokens;
    private readonly int _refillRate;
    private readonly TimeSpan _refillInterval;
    private readonly SemaphoreSlim _semaphore;
    private readonly ConcurrentDictionary<string, ProviderBucket> _buckets;
    
    /// <summary>
    /// Default rate limiter with conservative limits.
    /// </summary>
    public static RateLimiter Default { get; } = new(
        maxRequestsPerMinute: 60,
        maxConcurrentRequests: 5);
    
    /// <summary>
    /// Creates a new rate limiter.
    /// </summary>
    /// <param name="maxRequestsPerMinute">Maximum requests per minute per provider.</param>
    /// <param name="maxConcurrentRequests">Maximum concurrent requests.</param>
    public RateLimiter(int maxRequestsPerMinute = 60, int maxConcurrentRequests = 5)
    {
        _maxTokens = maxRequestsPerMinute;
        _refillRate = maxRequestsPerMinute;
        _refillInterval = TimeSpan.FromMinutes(1);
        _semaphore = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
        _buckets = new ConcurrentDictionary<string, ProviderBucket>();
    }
    
    /// <summary>
    /// Acquires a permit to make an API request.
    /// Blocks until a permit is available or cancellation is requested.
    /// </summary>
    /// <param name="provider">The LLM provider name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A disposable permit that should be disposed when the request completes.</returns>
    public async Task<IDisposable> AcquireAsync(string provider, CancellationToken cancellationToken = default)
    {
        // Wait for concurrent request slot
        await _semaphore.WaitAsync(cancellationToken);
        
        try
        {
            // Get or create bucket for this provider
            var bucket = _buckets.GetOrAdd(provider, _ => new ProviderBucket(_maxTokens, _refillRate, _refillInterval));
            
            // Wait for token from bucket
            await bucket.WaitForTokenAsync(cancellationToken);
            
            return new Permit(_semaphore);
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }
    
    /// <summary>
    /// Tries to acquire a permit without blocking.
    /// </summary>
    /// <param name="provider">The LLM provider name.</param>
    /// <param name="permit">The acquired permit if successful.</param>
    /// <returns>True if a permit was acquired, false otherwise.</returns>
    public bool TryAcquire(string provider, out IDisposable? permit)
    {
        permit = null;
        
        if (!_semaphore.Wait(0))
            return false;
        
        try
        {
            var bucket = _buckets.GetOrAdd(provider, _ => new ProviderBucket(_maxTokens, _refillRate, _refillInterval));
            
            if (!bucket.TryTakeToken())
            {
                _semaphore.Release();
                return false;
            }
            
            permit = new Permit(_semaphore);
            return true;
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }
    
    /// <summary>
    /// Gets the current available tokens for a provider.
    /// </summary>
    public int GetAvailableTokens(string provider)
    {
        if (_buckets.TryGetValue(provider, out var bucket))
        {
            return bucket.AvailableTokens;
        }
        return _maxTokens;
    }
    
    /// <summary>
    /// Gets the estimated wait time until a token is available.
    /// </summary>
    public TimeSpan GetEstimatedWaitTime(string provider)
    {
        if (_buckets.TryGetValue(provider, out var bucket))
        {
            return bucket.EstimatedWaitTime;
        }
        return TimeSpan.Zero;
    }
    
    private sealed class Permit : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;
        
        public Permit(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _semaphore.Release();
            }
        }
    }
    
    private sealed class ProviderBucket
    {
        private readonly int _maxTokens;
        private readonly int _refillRate;
        private readonly TimeSpan _refillInterval;
        private readonly object _lock = new();
        private double _tokens;
        private DateTime _lastRefill;
        
        public ProviderBucket(int maxTokens, int refillRate, TimeSpan refillInterval)
        {
            _maxTokens = maxTokens;
            _refillRate = refillRate;
            _refillInterval = refillInterval;
            _tokens = maxTokens;
            _lastRefill = DateTime.UtcNow;
        }
        
        public int AvailableTokens
        {
            get
            {
                lock (_lock)
                {
                    Refill();
                    return (int)_tokens;
                }
            }
        }
        
        public TimeSpan EstimatedWaitTime
        {
            get
            {
                lock (_lock)
                {
                    Refill();
                    if (_tokens >= 1)
                        return TimeSpan.Zero;
                    
                    var tokensNeeded = 1 - _tokens;
                    var secondsPerToken = _refillInterval.TotalSeconds / _refillRate;
                    return TimeSpan.FromSeconds(tokensNeeded * secondsPerToken);
                }
            }
        }
        
        public async Task WaitForTokenAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                lock (_lock)
                {
                    Refill();
                    if (_tokens >= 1)
                    {
                        _tokens -= 1;
                        return;
                    }
                }
                
                // Wait a bit and try again
                await Task.Delay(100, cancellationToken);
            }
        }
        
        public bool TryTakeToken()
        {
            lock (_lock)
            {
                Refill();
                if (_tokens >= 1)
                {
                    _tokens -= 1;
                    return true;
                }
                return false;
            }
        }
        
        private void Refill()
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastRefill;
            var tokensToAdd = elapsed.TotalSeconds / _refillInterval.TotalSeconds * _refillRate;
            
            _tokens = Math.Min(_maxTokens, _tokens + tokensToAdd);
            _lastRefill = now;
        }
    }
}
