using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Tsugix.Core;
using Tsugix.Logging;
using Tsugix.Telemetry;

namespace Tsugix.ContextEngine;

/// <summary>
/// Orchestrates error parsing and source context extraction.
/// </summary>
public class ContextEngine : IContextEngine
{
    private readonly ParserRegistry _parserRegistry;
    private readonly ISourceReader _sourceReader;
    private readonly int _contextLines;
    private readonly ILogger<ContextEngine> _logger;
    
    public ContextEngine(ParserRegistry parserRegistry, ISourceReader sourceReader, int contextLines = 10)
    {
        _parserRegistry = parserRegistry ?? throw new ArgumentNullException(nameof(parserRegistry));
        _sourceReader = sourceReader ?? throw new ArgumentNullException(nameof(sourceReader));
        _contextLines = contextLines;
        _logger = TsugixLogger.CreateLogger<ContextEngine>();
    }
    
    /// <summary>
    /// Creates a ContextEngine with all default parsers registered.
    /// </summary>
    public static ContextEngine CreateDefault(int contextLines = 10)
    {
        var registry = ParserRegistry.CreateWithDefaultParsers();
        var sourceReader = new SourceReader();
        return new ContextEngine(registry, sourceReader, contextLines);
    }
    
    /// <summary>
    /// Processes a crash report and extracts full error context.
    /// </summary>
    public ErrorContext? Process(CrashReport crashReport)
    {
        if (crashReport == null || string.IsNullOrEmpty(crashReport.Stderr))
            return null;
        
        return ProcessStderr(
            crashReport.Stderr, 
            crashReport.WorkingDirectory,
            crashReport.Command,
            crashReport.Timestamp);
    }
    
    /// <summary>
    /// Processes raw stderr and extracts error context.
    /// </summary>
    public ErrorContext? ProcessStderr(
        string stderr, 
        string? workingDirectory = null,
        string? originalCommand = null,
        DateTimeOffset? timestamp = null)
    {
        if (string.IsNullOrEmpty(stderr))
            return null;
        
        var stopwatch = Stopwatch.StartNew();
        
        // Find the best parser
        var parser = _parserRegistry.GetBestParser(stderr);
        if (parser == null)
        {
            _logger.LogDebug(LogEvents.ParsingFailed, "No suitable parser found for stderr");
            return CreateFallbackContext(stderr, workingDirectory, originalCommand, timestamp);
        }
        
        _logger.LogDebug(LogEvents.ParserSelected, "Selected parser: {Parser} for language: {Language}", 
            parser.GetType().Name, parser.LanguageName);
        
        // Parse the error
        var parseResult = parser.Parse(stderr);
        
        stopwatch.Stop();
        TsugixMetrics.Instance.RecordParsingDuration(parser.LanguageName, stopwatch.Elapsed.TotalMilliseconds);
        
        if (!parseResult.Success)
        {
            _logger.LogDebug(LogEvents.ParsingFailed, "Parser failed to extract error information");
            return CreateFallbackContext(stderr, workingDirectory, originalCommand, timestamp);
        }
        
        _logger.LogDebug(LogEvents.ErrorParsed, 
            "Parsed {Language} error: {ExceptionType} with {FrameCount} frames",
            parser.LanguageName, parseResult.Exception?.Type, parseResult.Frames.Count);
        
        // Identify primary frame (first user code frame)
        var primaryFrame = IdentifyPrimaryFrame(parseResult.Frames);
        
        // Enrich frames with source context
        var enrichedFrames = EnrichFramesWithSource(parseResult.Frames, workingDirectory);
        
        if (primaryFrame != null)
        {
            _logger.LogDebug(LogEvents.SourceContextLoaded, 
                "Primary frame: {FilePath}:{LineNumber}", 
                primaryFrame.FilePath, primaryFrame.LineNumber);
        }
        
        return new ErrorContext
        {
            Language = parser.LanguageName,
            Exception = parseResult.Exception!,
            Frames = enrichedFrames,
            PrimaryFrame = primaryFrame != null 
                ? enrichedFrames.FirstOrDefault(f => 
                    f.FilePath == primaryFrame.FilePath && 
                    f.LineNumber == primaryFrame.LineNumber)
                : enrichedFrames.FirstOrDefault(),
            OriginalCommand = originalCommand ?? "unknown",
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            Timestamp = timestamp ?? DateTimeOffset.Now
        };
    }
    
    private StackFrame? IdentifyPrimaryFrame(IReadOnlyList<StackFrame> frames)
    {
        return frames.FirstOrDefault(f => f.IsUserCode && !string.IsNullOrEmpty(f.FilePath));
    }
    
    private IReadOnlyList<StackFrame> EnrichFramesWithSource(
        IReadOnlyList<StackFrame> frames,
        string? workingDirectory)
    {
        var enrichedFrames = new List<StackFrame>();
        
        foreach (var frame in frames)
        {
            var enrichedFrame = frame;
            
            if (!string.IsNullOrEmpty(frame.FilePath) && frame.LineNumber.HasValue)
            {
                var filePath = ResolveFilePath(frame.FilePath, workingDirectory);
                var sourceContext = _sourceReader.ReadContext(filePath, frame.LineNumber.Value, _contextLines);
                
                if (sourceContext != null)
                {
                    enrichedFrame = frame with { SourceContext = sourceContext };
                }
            }
            
            enrichedFrames.Add(enrichedFrame);
        }
        
        return enrichedFrames;
    }
    
    private static string ResolveFilePath(string filePath, string? workingDirectory)
    {
        if (Path.IsPathRooted(filePath))
            return filePath;
        
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            var combined = Path.Combine(workingDirectory, filePath);
            if (File.Exists(combined))
                return combined;
        }
        
        return filePath;
    }
    
    private static ErrorContext CreateFallbackContext(
        string stderr, 
        string? workingDirectory,
        string? originalCommand,
        DateTimeOffset? timestamp)
    {
        return new ErrorContext
        {
            Language = "Unknown",
            Exception = new ExceptionInfo
            {
                Type = "Error",
                Message = stderr.Length > 200 ? stderr[..200] + "..." : stderr
            },
            Frames = Array.Empty<StackFrame>(),
            PrimaryFrame = null,
            OriginalCommand = originalCommand ?? "unknown",
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            Timestamp = timestamp ?? DateTimeOffset.Now
        };
    }
}
