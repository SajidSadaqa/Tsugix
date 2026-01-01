using System.Text.RegularExpressions;

namespace Tsugix.ContextEngine.Parsers;

/// <summary>
/// Parser for Go panics and errors.
/// Handles Go stack traces with "goroutine N [running]:" and file:line format.
/// </summary>
public partial class GoErrorParser : IErrorParser
{
    public string LanguageName => "Go";
    
    // Panic header: panic: message
    [GeneratedRegex(@"^panic:\s+(?<message>.+)$", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex PanicHeaderRegex();
    
    // Goroutine header: goroutine N [running]:
    [GeneratedRegex(@"^goroutine\s+\d+\s+\[(?<state>\w+)\]:", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex GoroutineHeaderRegex();
    
    // Stack frame: package.function(args)
    //              /path/to/file.go:line +offset
    [GeneratedRegex(@"^(?<func>[\w./]+(?:\(.*?\))?)\s*\n\s*(?<path>[^\s:]+\.go):(?<line>\d+)",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex FramePatternRegex();
    
    // Simple frame: /path/to/file.go:line
    [GeneratedRegex(@"^\s*(?<path>[^\s:]+\.go):(?<line>\d+)", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex SimpleFrameRegex();
    
    public ParserConfidence CanParse(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return ParserConfidence.None;
        
        if (PanicHeaderRegex().IsMatch(stderr) || GoroutineHeaderRegex().IsMatch(stderr))
            return ParserConfidence.High;
        
        if (stderr.Contains(".go:") && (stderr.Contains("goroutine") || stderr.Contains("panic")))
            return ParserConfidence.Medium;
        
        if (stderr.Contains(".go:"))
            return ParserConfidence.Low;
        
        return ParserConfidence.None;
    }
    
    public ParseResult Parse(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return ParseResult.Failed(stderr);
        
        try
        {
            var exception = ParsePanic(stderr);
            var frames = ParseFrames(stderr);
            
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
    
    private ExceptionInfo? ParsePanic(string stderr)
    {
        var match = PanicHeaderRegex().Match(stderr);
        if (match.Success)
        {
            return new ExceptionInfo
            {
                Type = "panic",
                Message = match.Groups["message"].Value.Trim()
            };
        }
        return null;
    }
    
    private List<StackFrame> ParseFrames(string stderr)
    {
        var frames = new List<StackFrame>();
        
        // Try complex frame pattern first
        foreach (Match match in FramePatternRegex().Matches(stderr))
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
                    FunctionName = ExtractFunctionName(funcName),
                    IsUserCode = !IsStandardLibrary(filePath)
                });
            }
        }
        
        // If no complex frames found, try simple pattern
        if (frames.Count == 0)
        {
            foreach (Match match in SimpleFrameRegex().Matches(stderr))
            {
                var filePath = match.Groups["path"].Value.Trim();
                var lineStr = match.Groups["line"].Value.Trim();
                
                if (int.TryParse(lineStr, out var lineNumber))
                {
                    frames.Add(new StackFrame
                    {
                        FilePath = filePath,
                        LineNumber = lineNumber,
                        IsUserCode = !IsStandardLibrary(filePath)
                    });
                }
            }
        }
        
        return frames;
    }
    
    private static string ExtractFunctionName(string fullFunc)
    {
        var parenIndex = fullFunc.IndexOf('(');
        var funcWithoutArgs = parenIndex > 0 ? fullFunc[..parenIndex] : fullFunc;
        var lastDot = funcWithoutArgs.LastIndexOf('.');
        return lastDot > 0 ? funcWithoutArgs[(lastDot + 1)..] : funcWithoutArgs;
    }
    
    private static bool IsStandardLibrary(string path)
    {
        return path.Contains("/go/src/") ||
               path.Contains("\\go\\src\\") ||
               path.Contains("/pkg/mod/") ||
               path.Contains("\\pkg\\mod\\") ||
               path.StartsWith("runtime/") ||
               path.StartsWith("net/") ||
               path.StartsWith("fmt/");
    }
}
