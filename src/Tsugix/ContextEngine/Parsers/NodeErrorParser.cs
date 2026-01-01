using System.Text.RegularExpressions;

namespace Tsugix.ContextEngine.Parsers;

/// <summary>
/// Parser for Node.js/JavaScript errors.
/// Handles V8 stack traces with "at function (file:line:col)" format.
/// </summary>
public partial class NodeErrorParser : IErrorParser
{
    public string LanguageName => "Node.js";
    
    // Error header pattern: ErrorType: message
    [GeneratedRegex(@"^(?<type>\w*(?:Error|Exception)): (?<message>.+)$", 
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex ErrorHeaderRegex();
    
    // Stack frame pattern: at function (file:line:col) or at file:line:col
    // Handles both Unix and Windows paths
    [GeneratedRegex(@"^\s*at\s+(?:(?<func>[^\s(]+)\s+)?\(?(?<path>(?:[A-Za-z]:)?[^:]+):(?<line>\d+):(?<col>\d+)\)?",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex FramePatternRegex();
    
    // Anonymous function pattern: at Object.<anonymous> (file:line:col)
    [GeneratedRegex(@"^\s*at\s+(?<context>\w+)\.<anonymous>\s+\((?<path>[^:]+):(?<line>\d+):(?<col>\d+)\)",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex AnonymousFrameRegex();
    
    // Native frame pattern: at native
    [GeneratedRegex(@"^\s*at\s+(?<func>\w+)\s+\(native\)", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex NativeFrameRegex();
    
    public ParserConfidence CanParse(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return ParserConfidence.None;
        
        // High confidence if we see V8-style stack frames
        if (FramePatternRegex().IsMatch(stderr) || AnonymousFrameRegex().IsMatch(stderr))
            return ParserConfidence.High;
        
        // Medium confidence if we see common JS error types
        if (ErrorHeaderRegex().IsMatch(stderr))
            return ParserConfidence.Medium;
        
        // Low confidence for generic error patterns
        if (stderr.Contains("TypeError:") || stderr.Contains("ReferenceError:") || 
            stderr.Contains("SyntaxError:") || stderr.Contains("RangeError:"))
            return ParserConfidence.Low;
        
        return ParserConfidence.None;
    }
    
    public ParseResult Parse(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return ParseResult.Failed(stderr);
        
        try
        {
            var exception = ParseException(stderr);
            var frames = ParseFrames(stderr);
            
            if (exception == null && frames.Count == 0)
                return ParseResult.Failed(stderr);
            
            return new ParseResult
            {
                Success = true,
                Exception = exception ?? new ExceptionInfo { Type = "Error", Message = "Unknown error" },
                Frames = frames,
                RawError = stderr
            };
        }
        catch
        {
            return ParseResult.Failed(stderr);
        }
    }

    private ExceptionInfo? ParseException(string stderr)
    {
        var lines = stderr.Split('\n');
        
        // Look for error header at the beginning
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;
            
            var match = ErrorHeaderRegex().Match(trimmed);
            if (match.Success)
            {
                return new ExceptionInfo
                {
                    Type = match.Groups["type"].Value.Trim(),
                    Message = match.Groups["message"].Value.Trim()
                };
            }
            
            // Stop if we hit a stack frame
            if (trimmed.StartsWith("at "))
                break;
        }
        
        return null;
    }
    
    private List<StackFrame> ParseFrames(string stderr)
    {
        var frames = new List<StackFrame>();
        
        // Try standard frame pattern
        foreach (Match match in FramePatternRegex().Matches(stderr))
        {
            var filePath = match.Groups["path"].Value.Trim();
            var lineStr = match.Groups["line"].Value.Trim();
            var colStr = match.Groups["col"].Value.Trim();
            var funcName = match.Groups["func"].Success ? match.Groups["func"].Value.Trim() : null;
            
            if (int.TryParse(lineStr, out var lineNumber))
            {
                int.TryParse(colStr, out var colNumber);
                
                frames.Add(new StackFrame
                {
                    FilePath = filePath,
                    LineNumber = lineNumber,
                    ColumnNumber = colNumber > 0 ? colNumber : null,
                    FunctionName = funcName,
                    IsUserCode = !IsLibraryPath(filePath)
                });
            }
        }
        
        // Try anonymous frame pattern
        foreach (Match match in AnonymousFrameRegex().Matches(stderr))
        {
            var filePath = match.Groups["path"].Value.Trim();
            var lineStr = match.Groups["line"].Value.Trim();
            var colStr = match.Groups["col"].Value.Trim();
            var context = match.Groups["context"].Value.Trim();
            
            if (int.TryParse(lineStr, out var lineNumber))
            {
                int.TryParse(colStr, out var colNumber);
                
                // Avoid duplicates
                if (!frames.Any(f => f.FilePath == filePath && f.LineNumber == lineNumber))
                {
                    frames.Add(new StackFrame
                    {
                        FilePath = filePath,
                        LineNumber = lineNumber,
                        ColumnNumber = colNumber > 0 ? colNumber : null,
                        FunctionName = $"{context}.<anonymous>",
                        IsUserCode = !IsLibraryPath(filePath)
                    });
                }
            }
        }
        
        return frames;
    }
    
    private static bool IsLibraryPath(string path)
    {
        return path.Contains("node_modules") ||
               path.Contains("internal/") ||
               path.Contains("internal\\") ||
               path.StartsWith("node:") ||
               path.Contains("/node/") ||
               path.Contains("\\node\\") ||
               path.StartsWith("<");
    }
}
