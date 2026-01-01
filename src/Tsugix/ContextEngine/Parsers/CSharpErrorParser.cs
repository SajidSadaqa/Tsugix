using System.Text.RegularExpressions;

namespace Tsugix.ContextEngine.Parsers;

/// <summary>
/// Parser for C#/.NET exceptions.
/// Handles .NET stack traces with "at Namespace.Class.Method() in file:line N" format.
/// </summary>
public partial class CSharpErrorParser : IErrorParser
{
    public string LanguageName => "C#";
    
    // Exception header pattern: System.ExceptionType: message
    [GeneratedRegex(@"^(?<type>[\w.]+(?:Exception|Error)): (?<message>.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex ExceptionHeaderRegex();
    
    // Stack frame pattern: at Namespace.Class.Method() in file:line N
    [GeneratedRegex(@"^\s*at\s+(?<method>[\w.<>]+(?:\(.*?\))?)\s+in\s+(?<path>(?:[A-Za-z]:)?[^:]+):line\s+(?<line>\d+)",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex FrameWithFileRegex();
    
    // Stack frame without file info: at Namespace.Class.Method()
    [GeneratedRegex(@"^\s*at\s+(?<method>[\w.<>`\[\],\s]+(?:\(.*?\))?)",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex FrameWithoutFileRegex();
    
    // Inner exception pattern
    [GeneratedRegex(@"--->\s+(?<type>[\w.]+(?:Exception|Error)): (?<message>.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex InnerExceptionRegex();
    
    public ParserConfidence CanParse(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return ParserConfidence.None;
        
        // High confidence if we see .NET-style stack frames with file info
        if (FrameWithFileRegex().IsMatch(stderr))
            return ParserConfidence.High;
        
        // High confidence if we see .NET exception types
        if (ExceptionHeaderRegex().IsMatch(stderr))
            return ParserConfidence.High;
        
        // Medium confidence if we see "at" frames with namespaces
        if (stderr.Contains("at System.") || stderr.Contains("at Microsoft."))
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
        var lines = stderr.Split('\n');
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;
            
            var match = ExceptionHeaderRegex().Match(trimmed);
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
        
        // First try frames with file info
        foreach (Match match in FrameWithFileRegex().Matches(stderr))
        {
            var method = match.Groups["method"].Value.Trim();
            var filePath = match.Groups["path"].Value.Trim();
            var lineStr = match.Groups["line"].Value.Trim();
            
            if (int.TryParse(lineStr, out var lineNumber))
            {
                var (className, methodName) = ParseMethodSignature(method);
                
                frames.Add(new StackFrame
                {
                    FilePath = filePath,
                    LineNumber = lineNumber,
                    FunctionName = methodName,
                    ClassName = className,
                    IsUserCode = !IsLibraryPath(filePath) && !IsFrameworkClass(className)
                });
            }
        }
        
        return frames;
    }
    
    private static (string? ClassName, string MethodName) ParseMethodSignature(string method)
    {
        // Remove parameters
        var parenIndex = method.IndexOf('(');
        var methodWithoutParams = parenIndex > 0 ? method[..parenIndex] : method;
        
        // Split by last dot to get class and method
        var lastDot = methodWithoutParams.LastIndexOf('.');
        if (lastDot > 0)
        {
            return (methodWithoutParams[..lastDot], methodWithoutParams[(lastDot + 1)..]);
        }
        
        return (null, methodWithoutParams);
    }
    
    private static bool IsLibraryPath(string path)
    {
        return path.Contains("\\dotnet\\") ||
               path.Contains("/dotnet/") ||
               path.Contains("\\nuget\\") ||
               path.Contains("/nuget/") ||
               path.Contains("\\packages\\") ||
               path.Contains("/packages/");
    }
    
    private static bool IsFrameworkClass(string? className)
    {
        if (string.IsNullOrEmpty(className))
            return false;
        
        return className.StartsWith("System.") ||
               className.StartsWith("Microsoft.") ||
               className.StartsWith("Newtonsoft.") ||
               className.StartsWith("NUnit.") ||
               className.StartsWith("Xunit.");
    }
}
