using Microsoft.Extensions.Logging;

namespace Tsugix.Logging;

/// <summary>
/// Centralized logging infrastructure for Tsugix.
/// Provides structured logging with configurable verbosity levels.
/// </summary>
public static class TsugixLogger
{
    private static ILoggerFactory? _loggerFactory;
    private static LogLevel _minimumLevel = LogLevel.Warning;
    
    /// <summary>
    /// Initializes the logging infrastructure.
    /// </summary>
    /// <param name="verbose">Whether to enable verbose (Debug) logging.</param>
    public static void Initialize(bool verbose = false)
    {
        _minimumLevel = verbose ? LogLevel.Debug : LogLevel.Warning;
        
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(_minimumLevel)
                .AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "[HH:mm:ss] ";
                    options.IncludeScopes = false;
                });
        });
    }
    
    /// <summary>
    /// Creates a logger for the specified type.
    /// </summary>
    public static ILogger<T> CreateLogger<T>()
    {
        EnsureInitialized();
        return _loggerFactory!.CreateLogger<T>();
    }
    
    /// <summary>
    /// Creates a logger with the specified category name.
    /// </summary>
    public static ILogger CreateLogger(string categoryName)
    {
        EnsureInitialized();
        return _loggerFactory!.CreateLogger(categoryName);
    }
    
    /// <summary>
    /// Gets the current minimum log level.
    /// </summary>
    public static LogLevel MinimumLevel => _minimumLevel;
    
    /// <summary>
    /// Checks if verbose logging is enabled.
    /// </summary>
    public static bool IsVerbose => _minimumLevel <= LogLevel.Debug;
    
    private static void EnsureInitialized()
    {
        if (_loggerFactory == null)
        {
            Initialize(verbose: false);
        }
    }
    
    /// <summary>
    /// Disposes the logger factory.
    /// </summary>
    public static void Shutdown()
    {
        _loggerFactory?.Dispose();
        _loggerFactory = null;
    }
}

/// <summary>
/// Log event IDs for structured logging.
/// </summary>
public static class LogEvents
{
    // Process execution events (1000-1999)
    public static readonly EventId ProcessStarted = new(1001, "ProcessStarted");
    public static readonly EventId ProcessCompleted = new(1002, "ProcessCompleted");
    public static readonly EventId ProcessFailed = new(1003, "ProcessFailed");
    public static readonly EventId ProcessCancelled = new(1004, "ProcessCancelled");
    public static readonly EventId ProcessNotFound = new(1005, "ProcessNotFound");
    
    // Context engine events (2000-2999)
    public static readonly EventId ParserSelected = new(2001, "ParserSelected");
    public static readonly EventId ErrorParsed = new(2002, "ErrorParsed");
    public static readonly EventId SourceContextLoaded = new(2003, "SourceContextLoaded");
    public static readonly EventId ParsingFailed = new(2004, "ParsingFailed");
    
    // AI Surgeon events (3000-3999)
    public static readonly EventId LlmRequestStarted = new(3001, "LlmRequestStarted");
    public static readonly EventId LlmRequestCompleted = new(3002, "LlmRequestCompleted");
    public static readonly EventId LlmRequestFailed = new(3003, "LlmRequestFailed");
    public static readonly EventId LlmRequestRetry = new(3004, "LlmRequestRetry");
    public static readonly EventId FixSuggestionParsed = new(3005, "FixSuggestionParsed");
    public static readonly EventId FixApplied = new(3006, "FixApplied");
    public static readonly EventId FixRejected = new(3007, "FixRejected");
    public static readonly EventId BackupCreated = new(3008, "BackupCreated");
    
    // Configuration events (4000-4999)
    public static readonly EventId ConfigLoaded = new(4001, "ConfigLoaded");
    public static readonly EventId ConfigNotFound = new(4002, "ConfigNotFound");
    public static readonly EventId ApiKeyMissing = new(4003, "ApiKeyMissing");
}
