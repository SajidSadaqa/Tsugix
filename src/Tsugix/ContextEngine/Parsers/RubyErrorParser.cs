using System.Text.RegularExpressions;

namespace Tsugix.ContextEngine.Parsers;

/// <summary>
/// Parser for Ruby exceptions.
/// Handles Ruby stack traces with "from file.rb:line:in 'method'" format.
/// </summary>
public partial class RubyErrorParser : IErrorParser
{
    public string LanguageName => "Ruby";
    
    // Exception header: file.rb:line:in 'method': message (ExceptionType)
    [GeneratedRegex(@"^(?<file>[^:]+\.rb):(?<line>\d+):in `(?<method>[^']+)':\s*(?<message>.+)\s*\((?<type>\w+(?:Error|Exception)?)\)",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex InlineExceptionRegex();
    
    // Simple exception: ExceptionType: message
    [GeneratedRegex(@"^(?<type>\w+(?:Error|Exception)?):\s*(?<message>.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex SimpleExceptionRegex();
    
    // Stack frame: from file.rb:line:in 'method'
    [GeneratedRegex(@"^\s*from\s+(?<path>[^:]+):(?<line>\d+):in\s+`(?<method>[^']+)'",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex FramePatternRegex();
    
    // Alternative frame format: file.rb:line:in 'method'
    [GeneratedRegex(@"^\s*(?<path>[^:]+\.rb):(?<line>\d+):in\s+`(?<method>[^']+)'",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex AltFramePatternRegex();
    
    public ParserConfidence CanParse(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return ParserConfidence.None;
        
        if (FramePatternRegex().IsMatch(stderr) || InlineExceptionRegex().IsMatch(stderr))
            return ParserConfidence.High;
        
        if (stderr.Contains(".rb:") && stderr.Contains(":in `"))
            return ParserConfidence.High;
        
        if (stderr.Contains("NoMethodError") || stderr.Contains("NameError") ||
            stderr.Contains("TypeError") || stderr.Contains("ArgumentError"))
            return ParserConfidence.Medium;
        
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
                Exception = exception ?? new ExceptionInfo { Type = "RuntimeError", Message = "Unknown error" },
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
        // Try inline format first
        var inlineMatch = InlineExceptionRegex().Match(stderr);
        if (inlineMatch.Success)
        {
            return new ExceptionInfo
            {
                Type = inlineMatch.Groups["type"].Value.Trim(),
                Message = inlineMatch.Groups["message"].Value.Trim()
            };
        }
        
        // Try simple format
        var simpleMatch = SimpleExceptionRegex().Match(stderr);
        if (simpleMatch.Success)
        {
            return new ExceptionInfo
            {
                Type = simpleMatch.Groups["type"].Value.Trim(),
                Message = simpleMatch.Groups["message"].Value.Trim()
            };
        }
        
        return null;
    }
    
    private List<StackFrame> ParseFrames(string stderr)
    {
        var frames = new List<StackFrame>();
        var seen = new HashSet<(string, int)>();
        
        // Parse "from" frames
        foreach (Match match in FramePatternRegex().Matches(stderr))
        {
            AddFrame(frames, seen, match);
        }
        
        // Parse alternative format frames
        foreach (Match match in AltFramePatternRegex().Matches(stderr))
        {
            AddFrame(frames, seen, match);
        }
        
        return frames;
    }
    
    private void AddFrame(List<StackFrame> frames, HashSet<(string, int)> seen, Match match)
    {
        var filePath = match.Groups["path"].Value.Trim();
        var lineStr = match.Groups["line"].Value.Trim();
        var methodName = match.Groups["method"].Value.Trim();
        
        if (int.TryParse(lineStr, out var lineNumber))
        {
            var key = (filePath, lineNumber);
            if (!seen.Contains(key))
            {
                seen.Add(key);
                frames.Add(new StackFrame
                {
                    FilePath = filePath,
                    LineNumber = lineNumber,
                    FunctionName = methodName,
                    IsUserCode = !IsLibraryPath(filePath)
                });
            }
        }
    }
    
    private static bool IsLibraryPath(string path)
    {
        return path.Contains("/gems/") ||
               path.Contains("\\gems\\") ||
               path.Contains("/ruby/") ||
               path.Contains("\\ruby\\") ||
               path.Contains("/lib/ruby/");
    }
}
