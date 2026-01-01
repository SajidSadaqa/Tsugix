using System.Text.RegularExpressions;

namespace Tsugix.ContextEngine.Parsers;

/// <summary>
/// Parser for Java exceptions.
/// Handles Java stack traces with "at package.Class.method(File.java:line)" format.
/// </summary>
public partial class JavaErrorParser : IErrorParser
{
    public string LanguageName => "Java";
    
    // Exception header: java.lang.ExceptionType: message
    [GeneratedRegex(@"^(?<type>[\w.]+(?:Exception|Error|Throwable)): (?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex ExceptionHeaderRegex();
    
    // Stack frame: at package.Class.method(File.java:line)
    [GeneratedRegex(@"^\s*at\s+(?<class>[\w.$]+)\.(?<method>[\w<>$]+)\((?<file>[\w.]+):(?<line>\d+)\)",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex FramePatternRegex();
    
    // Native method frame: at package.Class.method(Native Method)
    [GeneratedRegex(@"^\s*at\s+(?<class>[\w.$]+)\.(?<method>[\w<>$]+)\(Native Method\)",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex NativeFrameRegex();
    
    // Caused by pattern
    [GeneratedRegex(@"^Caused by:\s+(?<type>[\w.]+(?:Exception|Error)): (?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex CausedByRegex();
    
    public ParserConfidence CanParse(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return ParserConfidence.None;
        
        if (FramePatternRegex().IsMatch(stderr))
            return ParserConfidence.High;
        
        if (ExceptionHeaderRegex().IsMatch(stderr))
            return ParserConfidence.High;
        
        if (stderr.Contains("at java.") || stderr.Contains("at javax.") || 
            stderr.Contains("at org.") || stderr.Contains("at com."))
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
                Exception = exception ?? new ExceptionInfo { Type = "Exception", Message = "Unknown error" },
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
        var match = ExceptionHeaderRegex().Match(stderr);
        if (match.Success)
        {
            return new ExceptionInfo
            {
                Type = match.Groups["type"].Value.Trim(),
                Message = match.Groups["message"].Value.Trim()
            };
        }
        return null;
    }
    
    private List<StackFrame> ParseFrames(string stderr)
    {
        var frames = new List<StackFrame>();
        
        foreach (Match match in FramePatternRegex().Matches(stderr))
        {
            var className = match.Groups["class"].Value.Trim();
            var methodName = match.Groups["method"].Value.Trim();
            var fileName = match.Groups["file"].Value.Trim();
            var lineStr = match.Groups["line"].Value.Trim();
            
            if (int.TryParse(lineStr, out var lineNumber))
            {
                frames.Add(new StackFrame
                {
                    FilePath = fileName,
                    LineNumber = lineNumber,
                    FunctionName = methodName,
                    ClassName = className,
                    IsUserCode = !IsFrameworkClass(className)
                });
            }
        }
        
        return frames;
    }
    
    private static bool IsFrameworkClass(string className)
    {
        return className.StartsWith("java.") ||
               className.StartsWith("javax.") ||
               className.StartsWith("sun.") ||
               className.StartsWith("jdk.") ||
               className.StartsWith("org.springframework.") ||
               className.StartsWith("org.apache.");
    }
}
