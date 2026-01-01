using System.Text.RegularExpressions;

namespace Tsugix.ContextEngine.Parsers;

/// <summary>
/// Parser for Swift fatal errors and crashes.
/// Handles Swift stack traces with fatal error, precondition failure, and assertion failure formats.
/// </summary>
public partial class SwiftErrorParser : IErrorParser
{
    public string LanguageName => "Swift";
    
    // Fatal error: message: file file.swift, line N
    [GeneratedRegex(@"^Fatal error:\s*(?<message>.+?):\s*file\s+(?<file>[^,]+),\s*line\s+(?<line>\d+)",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex FatalErrorRegex();
    
    // Precondition failed: message: file file.swift, line N
    [GeneratedRegex(@"^Precondition failed:\s*(?<message>.+?):\s*file\s+(?<file>[^,]+),\s*line\s+(?<line>\d+)",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex PreconditionFailedRegex();
    
    // Assertion failed: message: file file.swift, line N
    [GeneratedRegex(@"^Assertion failed:\s*(?<message>.+?):\s*file\s+(?<file>[^,]+),\s*line\s+(?<line>\d+)",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex AssertionFailedRegex();
    
    // Simple fatal error: Fatal error: message
    [GeneratedRegex(@"^Fatal error:\s*(?<message>.+)$", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex SimpleFatalErrorRegex();
    
    // Stack frame: N module address function + offset
    [GeneratedRegex(@"^\d+\s+(?<module>\S+)\s+0x[\da-fA-F]+\s+(?<func>.+?)(?:\s*\+\s*\d+)?$",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex FramePatternRegex();
    
    // Frame with file info: at file.swift:line
    [GeneratedRegex(@"at\s+(?<file>[^:]+\.swift):(?<line>\d+)", RegexOptions.Compiled)]
    private static partial Regex FileInfoRegex();
    
    public ParserConfidence CanParse(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return ParserConfidence.None;
        
        if (FatalErrorRegex().IsMatch(stderr) || PreconditionFailedRegex().IsMatch(stderr) ||
            AssertionFailedRegex().IsMatch(stderr))
            return ParserConfidence.High;
        
        if (stderr.Contains("Fatal error:") && stderr.Contains(".swift"))
            return ParserConfidence.High;
        
        if (stderr.Contains(".swift:") || stderr.Contains("Swift runtime"))
            return ParserConfidence.Medium;
        
        if (stderr.Contains("Precondition failed") || stderr.Contains("Assertion failed"))
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
            
            // Add error location as first frame
            if (errorFile != null && errorLine.HasValue)
            {
                var errorFrame = new StackFrame
                {
                    FilePath = errorFile,
                    LineNumber = errorLine.Value,
                    IsUserCode = !IsSystemLibrary(errorFile)
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
                Exception = exception ?? new ExceptionInfo { Type = "FatalError", Message = "Unknown error" },
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
        // Try fatal error with file info
        var fatalMatch = FatalErrorRegex().Match(stderr);
        if (fatalMatch.Success)
        {
            var file = fatalMatch.Groups["file"].Value.Trim();
            int.TryParse(fatalMatch.Groups["line"].Value, out var line);
            
            return (new ExceptionInfo
            {
                Type = "FatalError",
                Message = fatalMatch.Groups["message"].Value.Trim()
            }, file, line);
        }
        
        // Try precondition failed
        var precondMatch = PreconditionFailedRegex().Match(stderr);
        if (precondMatch.Success)
        {
            var file = precondMatch.Groups["file"].Value.Trim();
            int.TryParse(precondMatch.Groups["line"].Value, out var line);
            
            return (new ExceptionInfo
            {
                Type = "PreconditionFailure",
                Message = precondMatch.Groups["message"].Value.Trim()
            }, file, line);
        }
        
        // Try assertion failed
        var assertMatch = AssertionFailedRegex().Match(stderr);
        if (assertMatch.Success)
        {
            var file = assertMatch.Groups["file"].Value.Trim();
            int.TryParse(assertMatch.Groups["line"].Value, out var line);
            
            return (new ExceptionInfo
            {
                Type = "AssertionFailure",
                Message = assertMatch.Groups["message"].Value.Trim()
            }, file, line);
        }
        
        // Try simple fatal error
        var simpleMatch = SimpleFatalErrorRegex().Match(stderr);
        if (simpleMatch.Success)
        {
            return (new ExceptionInfo
            {
                Type = "FatalError",
                Message = simpleMatch.Groups["message"].Value.Trim()
            }, null, null);
        }
        
        return (null, null, null);
    }
    
    private List<StackFrame> ParseFrames(string stderr)
    {
        var frames = new List<StackFrame>();
        
        foreach (Match match in FramePatternRegex().Matches(stderr))
        {
            var module = match.Groups["module"].Value.Trim();
            var funcName = match.Groups["func"].Value.Trim();
            
            frames.Add(new StackFrame
            {
                FunctionName = funcName,
                ClassName = module,
                IsUserCode = !IsSystemModule(module)
            });
        }
        
        return frames;
    }
    
    private static bool IsSystemLibrary(string path)
    {
        return path.Contains("/Swift/") ||
               path.Contains("\\Swift\\") ||
               path.Contains("/Xcode/") ||
               path.Contains("\\Xcode\\") ||
               path.Contains("/usr/lib/");
    }
    
    private static bool IsSystemModule(string module)
    {
        return module.StartsWith("libswift") ||
               module.StartsWith("libsystem") ||
               module.StartsWith("Foundation") ||
               module.StartsWith("CoreFoundation") ||
               module.StartsWith("libdispatch") ||
               module == "dyld";
    }
}
