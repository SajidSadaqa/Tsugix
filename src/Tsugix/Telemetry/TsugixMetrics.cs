using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Tsugix.Telemetry;

/// <summary>
/// Metrics and telemetry for Tsugix operations.
/// Uses System.Diagnostics.Metrics for OpenTelemetry compatibility.
/// </summary>
public sealed class TsugixMetrics : IDisposable
{
    private readonly Meter _meter;
    
    // Counters
    private readonly Counter<long> _commandsExecuted;
    private readonly Counter<long> _commandsFailed;
    private readonly Counter<long> _llmRequests;
    private readonly Counter<long> _llmRequestsFailed;
    private readonly Counter<long> _fixesApplied;
    private readonly Counter<long> _fixesRejected;
    
    // Histograms
    private readonly Histogram<double> _commandDuration;
    private readonly Histogram<double> _llmRequestDuration;
    private readonly Histogram<double> _parsingDuration;
    
    // Gauges (using ObservableGauge)
    private int _activeCommands;
    
    /// <summary>
    /// Singleton instance for global metrics.
    /// </summary>
    public static TsugixMetrics Instance { get; } = new();
    
    private TsugixMetrics()
    {
        _meter = new Meter("Tsugix", "1.0.0");
        
        // Initialize counters
        _commandsExecuted = _meter.CreateCounter<long>(
            "tsugix.commands.executed",
            unit: "{command}",
            description: "Total number of commands executed");
        
        _commandsFailed = _meter.CreateCounter<long>(
            "tsugix.commands.failed",
            unit: "{command}",
            description: "Total number of commands that failed");
        
        _llmRequests = _meter.CreateCounter<long>(
            "tsugix.llm.requests",
            unit: "{request}",
            description: "Total number of LLM API requests");
        
        _llmRequestsFailed = _meter.CreateCounter<long>(
            "tsugix.llm.requests.failed",
            unit: "{request}",
            description: "Total number of failed LLM API requests");
        
        _fixesApplied = _meter.CreateCounter<long>(
            "tsugix.fixes.applied",
            unit: "{fix}",
            description: "Total number of fixes applied");
        
        _fixesRejected = _meter.CreateCounter<long>(
            "tsugix.fixes.rejected",
            unit: "{fix}",
            description: "Total number of fixes rejected by user");
        
        // Initialize histograms
        _commandDuration = _meter.CreateHistogram<double>(
            "tsugix.commands.duration",
            unit: "ms",
            description: "Duration of command execution");
        
        _llmRequestDuration = _meter.CreateHistogram<double>(
            "tsugix.llm.request.duration",
            unit: "ms",
            description: "Duration of LLM API requests");
        
        _parsingDuration = _meter.CreateHistogram<double>(
            "tsugix.parsing.duration",
            unit: "ms",
            description: "Duration of error parsing");
        
        // Initialize observable gauge
        _meter.CreateObservableGauge(
            "tsugix.commands.active",
            () => _activeCommands,
            unit: "{command}",
            description: "Number of currently executing commands");
    }
    
    /// <summary>
    /// Records a command execution.
    /// </summary>
    public void RecordCommandExecuted(string command, bool success, double durationMs)
    {
        var tags = new TagList
        {
            { "command", TruncateCommand(command) },
            { "success", success.ToString().ToLowerInvariant() }
        };
        
        _commandsExecuted.Add(1, tags);
        _commandDuration.Record(durationMs, tags);
        
        if (!success)
        {
            _commandsFailed.Add(1, tags);
        }
    }
    
    /// <summary>
    /// Records an LLM API request.
    /// </summary>
    public void RecordLlmRequest(string provider, string model, bool success, double durationMs, int? statusCode = null)
    {
        var tags = new TagList
        {
            { "provider", provider },
            { "model", model },
            { "success", success.ToString().ToLowerInvariant() }
        };
        
        if (statusCode.HasValue)
        {
            tags.Add("status_code", statusCode.Value.ToString());
        }
        
        _llmRequests.Add(1, tags);
        _llmRequestDuration.Record(durationMs, tags);
        
        if (!success)
        {
            _llmRequestsFailed.Add(1, tags);
        }
    }
    
    /// <summary>
    /// Records a fix application result.
    /// </summary>
    public void RecordFixResult(string language, bool applied)
    {
        var tags = new TagList
        {
            { "language", language }
        };
        
        if (applied)
        {
            _fixesApplied.Add(1, tags);
        }
        else
        {
            _fixesRejected.Add(1, tags);
        }
    }
    
    /// <summary>
    /// Records error parsing duration.
    /// </summary>
    public void RecordParsingDuration(string language, double durationMs)
    {
        var tags = new TagList
        {
            { "language", language }
        };
        
        _parsingDuration.Record(durationMs, tags);
    }
    
    /// <summary>
    /// Increments the active command count.
    /// </summary>
    public void IncrementActiveCommands() => Interlocked.Increment(ref _activeCommands);
    
    /// <summary>
    /// Decrements the active command count.
    /// </summary>
    public void DecrementActiveCommands() => Interlocked.Decrement(ref _activeCommands);
    
    /// <summary>
    /// Creates a scope that tracks command execution timing.
    /// </summary>
    public CommandScope StartCommand(string command) => new(this, command);
    
    /// <summary>
    /// Creates a scope that tracks LLM request timing.
    /// </summary>
    public LlmRequestScope StartLlmRequest(string provider, string model) => new(this, provider, model);
    
    private static string TruncateCommand(string command)
    {
        // Only keep the executable name for metrics
        var parts = command.Split(' ', 2);
        return parts[0];
    }
    
    public void Dispose()
    {
        _meter.Dispose();
    }
    
    /// <summary>
    /// Scope for tracking command execution.
    /// </summary>
    public struct CommandScope : IDisposable
    {
        private readonly TsugixMetrics _metrics;
        private readonly string _command;
        private readonly Stopwatch _stopwatch;
        private bool _success;
        
        internal CommandScope(TsugixMetrics metrics, string command)
        {
            _metrics = metrics;
            _command = command;
            _stopwatch = Stopwatch.StartNew();
            _success = false;
            _metrics.IncrementActiveCommands();
        }
        
        public void SetSuccess(bool success) => _success = success;
        
        public void Dispose()
        {
            _stopwatch.Stop();
            _metrics.DecrementActiveCommands();
            _metrics.RecordCommandExecuted(_command, _success, _stopwatch.Elapsed.TotalMilliseconds);
        }
    }
    
    /// <summary>
    /// Scope for tracking LLM request execution.
    /// </summary>
    public struct LlmRequestScope : IDisposable
    {
        private readonly TsugixMetrics _metrics;
        private readonly string _provider;
        private readonly string _model;
        private readonly Stopwatch _stopwatch;
        private bool _success;
        private int? _statusCode;
        
        internal LlmRequestScope(TsugixMetrics metrics, string provider, string model)
        {
            _metrics = metrics;
            _provider = provider;
            _model = model;
            _stopwatch = Stopwatch.StartNew();
            _success = false;
            _statusCode = null;
        }
        
        public void SetResult(bool success, int? statusCode = null)
        {
            _success = success;
            _statusCode = statusCode;
        }
        
        public void Dispose()
        {
            _stopwatch.Stop();
            _metrics.RecordLlmRequest(_provider, _model, _success, _stopwatch.Elapsed.TotalMilliseconds, _statusCode);
        }
    }
}
