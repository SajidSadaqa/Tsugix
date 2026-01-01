using System.Text.RegularExpressions;

namespace Tsugix.ContextEngine.Parsers;

/// <summary>
/// Parser for Rust panics.
/// Handles Rust stack traces with "thread 'main' panicked at" format.
/// </summary>
public partial class RustErrorParser : IErrorParser
{
    public string LanguageName => "Rust";
    
    // Panic header: thread 'name' panicked at 'message', file:line:col
    [GeneratedRegex(@"thread '(?<thread>[^']+)' panicked at '(?<message>[^']+)',\s*(?<file>[^:]+):(?<line>\d+):(?<col>\d+)",
        RegexOptions.Compiled)]
    private static partial Regex PanicHeaderRegex();
    
    // Alternative panic format (Rust 1.65+): thread 'name' panicked at file:line:col:
    [GeneratedRegex(@"thread '(?<thread>[^']+)' panicked at (?<file>[^:]+):(?<line>\d+):(?<col>\d+):",
        RegexOptions.Compiled)]
    private static partial Regex NewPanicHeaderRegex();
    
    // Backtrace frame: N: function_name at file:line:col
    [GeneratedRegex(@"^\s*\d+:\s+(?<func>[\w:<>]+)\s+at\s+(?<path>[^:]+):(?<line>\d+):(?<col>\d+)",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex BacktraceFrameRegex();
    
    // Simple backtrace frame: N: function_name
    [GeneratedRegex(@"^\s*\d+:\s+(?<func>[\w:<>]+)", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex SimpleBacktraceFrameRegex();
    
    public ParserConfidence CanParse(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return ParserConfidence.None;
        
        if (PanicHeaderRegex().IsMatch(stderr) || NewPanicHeaderRegex().IsMatch(stderr))
            return ParserConfidence.High;
        
        if (stderr.Contains("thread '") && stderr.Contains("panicked"))
            return ParserConfidence.High;
        
        if (stderr.Contains(".rs:") && stderr.Contains("panicked"))
            return ParserConfidence.Medium;
        
        if (stderr.Contains("RUST_BACKTRACE") || stderr.Contains("stack backtrace:"))
            return ParserConfidence.Medium;
        
        return ParserConfidence.None;
    }
    
    public ParseResult Parse(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return ParseResult.Failed(stderr);
        
        try
        {
            var (exception, panicFile, panicLine) = ParsePanic(stderr);
            var frames = ParseFrames(stderr);
            
            // Add panic location as first frame if not in backtrace
            if (panicFile != null && panicLine.HasValue)
            {
                var panicFrame = new StackFrame
                {
                    FilePath = panicFile,
                    LineNumber = panicLine.Value,
                    FunctionName = "panic",
                    IsUserCode = !IsStandardLibrary(panicFile)
                };
                
                if (!frames.Any(f => f.FilePath == panicFile && f.LineNumber == panicLine))
                {
                    frames.Insert(0, panicFrame);
                }
            }
            
            if (exception == null && frames.Count == 0)
                return ParseResult.Failed(stderr);
            
            return new ParseResult
            {
                Success = true,
                Exception = exception ?? new ExceptionInfo { Type = "panic", Message = "Unknown panic" },
                Frames = frames,
                RawError = stderr
            };
        }
        catch
        {
            return ParseResult.Failed(stderr);
        }
    }
    
    private (ExceptionInfo? Exception, string? File, int? Line) ParsePanic(string stderr)
    {
        var match = PanicHeaderRegex().Match(stderr);
        if (match.Success)
        {
            var file = match.Groups["file"].Value.Trim();
            int.TryParse(match.Groups["line"].Value, out var line);
            
            return (new ExceptionInfo
            {
                Type = "panic",
                Message = match.Groups["message"].Value.Trim()
            }, file, line);
        }
        
        var newMatch = NewPanicHeaderRegex().Match(stderr);
        if (newMatch.Success)
        {
            var file = newMatch.Groups["file"].Value.Trim();
            int.TryParse(newMatch.Groups["line"].Value, out var line);
            
            // Message is on the next line for new format
            var lines = stderr.Split('\n');
            var panicLineIndex = Array.FindIndex(lines, l => l.Contains("panicked at"));
            var message = panicLineIndex >= 0 && panicLineIndex + 1 < lines.Length 
                ? lines[panicLineIndex + 1].Trim() 
                : "panic occurred";
            
            return (new ExceptionInfo
            {
                Type = "panic",
                Message = message
            }, file, line);
        }
        
        return (null, null, null);
    }
    
    private List<StackFrame> ParseFrames(string stderr)
    {
        var frames = new List<StackFrame>();
        
        foreach (Match match in BacktraceFrameRegex().Matches(stderr))
        {
            var funcName = match.Groups["func"].Value.Trim();
            var filePath = match.Groups["path"].Value.Trim();
            var lineStr = match.Groups["line"].Value.Trim();
            
            if (int.TryParse(lineStr, out var lineNumber))
            {
                frames.Add(new StackFrame
                {
                    FilePath = filePath,
                    LineNumber = lineNumber,
                    FunctionName = funcName,
                    IsUserCode = !IsStandardLibrary(filePath)
                });
            }
        }
        
        return frames;
    }
    
    private static bool IsStandardLibrary(string path)
    {
        return path.Contains("/rustc/") ||
               path.Contains("\\rustc\\") ||
               path.Contains("/.cargo/") ||
               path.Contains("\\.cargo\\") ||
               path.StartsWith("std/") ||
               path.StartsWith("core/") ||
               path.StartsWith("alloc/");
    }
}
