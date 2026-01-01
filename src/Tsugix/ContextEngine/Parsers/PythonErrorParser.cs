using System.Text.RegularExpressions;

namespace Tsugix.ContextEngine.Parsers;

/// <summary>
/// Parser for Python tracebacks.
/// Handles standard Python exception format with "Traceback (most recent call last):" header.
/// </summary>
public partial class PythonErrorParser : IErrorParser
{
    public string LanguageName => "Python";
    
    // Traceback header detection
    [GeneratedRegex(@"Traceback \(most recent call last\):", RegexOptions.Compiled)]
    private static partial Regex TracebackHeaderRegex();
    
    // Stack frame pattern: File "path", line N, in function
    [GeneratedRegex(@"^\s*File ""(?<path>[^""]+)"", line (?<line>\d+)(?:, in (?<func>.+))?", 
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex FramePatternRegex();
    
    // Exception pattern: ExceptionType: message (at end of traceback)
    [GeneratedRegex(@"^(?<type>\w+(?:\.\w+)*(?:Error|Exception|Warning)?): (?<message>.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex ExceptionPatternRegex();
    
    // Simple exception pattern without message
    [GeneratedRegex(@"^(?<type>\w+(?:\.\w+)*(?:Error|Exception|Warning)?)$",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex SimpleExceptionPatternRegex();
    
    public ParserConfidence CanParse(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return ParserConfidence.None;
        
        // High confidence if we see the traceback header
        if (TracebackHeaderRegex().IsMatch(stderr))
            return ParserConfidence.High;
        
        // Medium confidence if we see Python-style file references
        if (FramePatternRegex().IsMatch(stderr))
            return ParserConfidence.Medium;
        
        // Low confidence if we see common Python exception types
        if (stderr.Contains("Error:") || stderr.Contains("Exception:"))
            return ParserConfidence.Low;
        
        return ParserConfidence.None;
    }
    
    public ParseResult Parse(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return ParseResult.Failed(stderr);
        
        try
        {
            var frames = ParseFrames(stderr);
            var exception = ParseException(stderr);
            
            if (exception == null && frames.Count == 0)
                return ParseResult.Failed(stderr);
            
            return new ParseResult
            {
                Success = true,
                Exception = exception ?? new ExceptionInfo { Type = "Unknown", Message = "Unknown error" },
                Frames = frames,
                RawError = stderr
            };
        }
        catch
        {
            return ParseResult.Failed(stderr);
        }
    }
    
    private List<StackFrame> ParseFrames(string stderr)
    {
        var frames = new List<StackFrame>();
        var matches = FramePatternRegex().Matches(stderr);
        
        foreach (Match match in matches)
        {
            var filePath = match.Groups["path"].Value.Trim();
            var lineStr = match.Groups["line"].Value.Trim();
            var funcName = match.Groups["func"].Success ? match.Groups["func"].Value.Trim() : null;
            
            if (int.TryParse(lineStr, out var lineNumber))
            {
                frames.Add(new StackFrame
                {
                    FilePath = filePath,
                    LineNumber = lineNumber,
                    FunctionName = funcName,
                    IsUserCode = !IsLibraryPath(filePath)
                });
            }
        }
        
        return frames;
    }
    
    private ExceptionInfo? ParseException(string stderr)
    {
        var lines = stderr.Split('\n');
        
        // Look for exception at the end (Python puts it last)
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
                continue;
            
            var match = ExceptionPatternRegex().Match(line);
            if (match.Success)
            {
                return new ExceptionInfo
                {
                    Type = match.Groups["type"].Value,
                    Message = match.Groups["message"].Value
                };
            }
            
            // Try simple pattern (exception type only, no message)
            var simpleMatch = SimpleExceptionPatternRegex().Match(line);
            if (simpleMatch.Success)
            {
                return new ExceptionInfo
                {
                    Type = simpleMatch.Groups["type"].Value,
                    Message = ""
                };
            }
            
            // Stop if we hit a frame line
            if (line.StartsWith("File ") || line.StartsWith("  File "))
                break;
        }
        
        return null;
    }
    
    private static bool IsLibraryPath(string path)
    {
        // Common library paths
        return path.Contains("site-packages") ||
               path.Contains("dist-packages") ||
               path.Contains("/lib/python") ||
               path.Contains("\\lib\\python") ||
               path.StartsWith("<");
    }
}
