using System.Text.RegularExpressions;

namespace Tsugix.ContextEngine.Parsers;

/// <summary>
/// Parser for PHP errors and exceptions.
/// Handles PHP stack traces with "Fatal error:", "Warning:", and exception formats.
/// </summary>
public partial class PhpErrorParser : IErrorParser
{
    public string LanguageName => "PHP";
    
    // Fatal error: message in file on line N
    [GeneratedRegex(@"^(?<type>Fatal error|Warning|Notice|Parse error):\s*(?<message>.+?)\s+in\s+(?<file>[^\s]+)\s+on\s+line\s+(?<line>\d+)",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex ErrorHeaderRegex();
    
    // Exception: message in file:line
    [GeneratedRegex(@"^(?<type>[\w\\]+Exception):\s*(?<message>.+?)\s+in\s+(?<file>[^:]+):(?<line>\d+)",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex ExceptionHeaderRegex();
    
    // Stack trace frame: #N file(line): function()
    [GeneratedRegex(@"^#\d+\s+(?<path>[^(]+)\((?<line>\d+)\):\s*(?<func>.+)",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex FramePatternRegex();
    
    // Alternative frame: #N {main}
    [GeneratedRegex(@"^#\d+\s+\{main\}", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex MainFrameRegex();
    
    public ParserConfidence CanParse(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return ParserConfidence.None;
        
        if (ErrorHeaderRegex().IsMatch(stderr) || ExceptionHeaderRegex().IsMatch(stderr))
            return ParserConfidence.High;
        
        if (FramePatternRegex().IsMatch(stderr))
            return ParserConfidence.High;
        
        if (stderr.Contains("Fatal error:") || stderr.Contains("Parse error:") ||
            stderr.Contains("Stack trace:") || stderr.Contains(".php"))
            return ParserConfidence.Medium;
        
        return ParserConfidence.None;
    }
    
    public ParseResult Parse(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return ParseResult.Failed(stderr);
        
        try
        {
            var (exception, errorFile, errorLine) = ParseError(stderr);
            var frames = ParseFrames(stderr);
            
            // Add error location as first frame if not in stack trace
            if (errorFile != null && errorLine.HasValue)
            {
                var errorFrame = new StackFrame
                {
                    FilePath = errorFile,
                    LineNumber = errorLine.Value,
                    IsUserCode = !IsLibraryPath(errorFile)
                };
                
                if (!frames.Any(f => f.FilePath == errorFile && f.LineNumber == errorLine))
                {
                    frames.Insert(0, errorFrame);
                }
            }
            
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
    
    private (ExceptionInfo? Exception, string? File, int? Line) ParseError(string stderr)
    {
        // Try error format first
        var errorMatch = ErrorHeaderRegex().Match(stderr);
        if (errorMatch.Success)
        {
            var file = errorMatch.Groups["file"].Value.Trim();
            int.TryParse(errorMatch.Groups["line"].Value, out var line);
            
            return (new ExceptionInfo
            {
                Type = errorMatch.Groups["type"].Value.Trim(),
                Message = errorMatch.Groups["message"].Value.Trim()
            }, file, line);
        }
        
        // Try exception format
        var exMatch = ExceptionHeaderRegex().Match(stderr);
        if (exMatch.Success)
        {
            var file = exMatch.Groups["file"].Value.Trim();
            int.TryParse(exMatch.Groups["line"].Value, out var line);
            
            return (new ExceptionInfo
            {
                Type = exMatch.Groups["type"].Value.Trim(),
                Message = exMatch.Groups["message"].Value.Trim()
            }, file, line);
        }
        
        return (null, null, null);
    }
    
    private List<StackFrame> ParseFrames(string stderr)
    {
        var frames = new List<StackFrame>();
        
        foreach (Match match in FramePatternRegex().Matches(stderr))
        {
            var filePath = match.Groups["path"].Value.Trim();
            var lineStr = match.Groups["line"].Value.Trim();
            var funcName = match.Groups["func"].Value.Trim();
            
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
    
    private static bool IsLibraryPath(string path)
    {
        return path.Contains("/vendor/") ||
               path.Contains("\\vendor\\") ||
               path.Contains("/pear/") ||
               path.Contains("\\pear\\") ||
               path.Contains("/php/") ||
               path.Contains("\\php\\");
    }
}
