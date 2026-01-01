using FsCheck;
using FsCheck.Xunit;
using Tsugix.ContextEngine;
using Tsugix.ContextEngine.Parsers;
using Xunit;

namespace Tsugix.Tests.ContextEngine;

/// <summary>
/// Property-based tests for all language parsers.
/// Feature: tsugix-phase2-context-engine
/// Validates: Requirements 5.x (Java), 6.x (Go), 7.x (Rust), 8.x (Ruby), 9.x (PHP), 10.x (Swift)
/// </summary>
public class LanguageParserTests
{
    #region Java Parser Tests
    
    private readonly JavaErrorParser _javaParser = new();
    
    [Fact]
    public void Java_RealWorldException_ParsesCorrectly()
    {
        var stderr = @"java.lang.NullPointerException: Cannot invoke method on null object
	at com.myapp.service.UserService.getUser(UserService.java:42)
	at com.myapp.controller.UserController.handleRequest(UserController.java:28)";
        
        var result = _javaParser.Parse(stderr);
        
        Assert.True(result.Success);
        Assert.Equal("java.lang.NullPointerException", result.Exception?.Type);
        Assert.Equal("Cannot invoke method on null object", result.Exception?.Message);
        Assert.Equal(2, result.Frames.Count);
        Assert.Equal("UserService.java", result.Frames[0].FilePath);
        Assert.Equal(42, result.Frames[0].LineNumber);
        Assert.Equal("getUser", result.Frames[0].FunctionName);
    }
    
    [Property(MaxTest = 50)]
    public Property Java_ValidException_ParsesSuccessfully()
    {
        var exTypes = new[] { "java.lang.NullPointerException", "java.lang.IllegalArgumentException" };
        var messages = new[] { "test error", "invalid argument" };
        
        return Prop.ForAll(
            Gen.Elements(exTypes).ToArbitrary(),
            Gen.Elements(messages).ToArbitrary(),
            Gen.Choose(1, 100).ToArbitrary(),
            (exType, message, line) =>
            {
                var stderr = $"{exType}: {message}\n\tat com.app.Main.run(Main.java:{line})";
                var result = _javaParser.Parse(stderr);
                
                return result.Success && 
                       result.Exception?.Type == exType &&
                       result.Frames.Count == 1 &&
                       result.Frames[0].LineNumber == line;
            });
    }
    
    #endregion
    
    #region Go Parser Tests
    
    private readonly GoErrorParser _goParser = new();
    
    [Fact]
    public void Go_Panic_ParsesCorrectly()
    {
        var stderr = @"panic: runtime error: index out of range

goroutine 1 [running]:
main.processData(0x0, 0x0)
	/home/user/project/main.go:42 +0x45
main.main()
	/home/user/project/main.go:15 +0x20";
        
        var result = _goParser.Parse(stderr);
        
        Assert.True(result.Success);
        Assert.Equal("panic", result.Exception?.Type);
        Assert.Contains("index out of range", result.Exception?.Message);
    }
    
    [Fact]
    public void Go_SimpleError_GivesConfidence()
    {
        var stderr = "error at /app/main.go:10";
        Assert.NotEqual(ParserConfidence.None, _goParser.CanParse(stderr));
    }
    
    #endregion
    
    #region Rust Parser Tests
    
    private readonly RustErrorParser _rustParser = new();
    
    [Fact]
    public void Rust_Panic_ParsesCorrectly()
    {
        var stderr = @"thread 'main' panicked at 'index out of bounds', src/main.rs:42:10
note: run with `RUST_BACKTRACE=1` environment variable to display a backtrace";
        
        var result = _rustParser.Parse(stderr);
        
        Assert.True(result.Success);
        Assert.Equal("panic", result.Exception?.Type);
        Assert.Equal("index out of bounds", result.Exception?.Message);
        Assert.Single(result.Frames);
        Assert.Equal("src/main.rs", result.Frames[0].FilePath);
        Assert.Equal(42, result.Frames[0].LineNumber);
    }
    
    [Fact]
    public void Rust_PanicHeader_GivesHighConfidence()
    {
        var stderr = "thread 'main' panicked at 'test', src/lib.rs:1:1";
        Assert.Equal(ParserConfidence.High, _rustParser.CanParse(stderr));
    }
    
    #endregion

    #region Ruby Parser Tests
    
    private readonly RubyErrorParser _rubyParser = new();
    
    [Fact]
    public void Ruby_Exception_ParsesCorrectly()
    {
        var stderr = @"app.rb:42:in `process': undefined method `foo' for nil:NilClass (NoMethodError)
	from app.rb:28:in `main'
	from app.rb:5:in `<main>'";
        
        var result = _rubyParser.Parse(stderr);
        
        Assert.True(result.Success);
        Assert.Equal("NoMethodError", result.Exception?.Type);
        Assert.True(result.Frames.Count >= 2);
    }
    
    [Fact]
    public void Ruby_StackFrame_GivesHighConfidence()
    {
        var stderr = "from /app/lib/service.rb:10:in `call'";
        Assert.Equal(ParserConfidence.High, _rubyParser.CanParse(stderr));
    }
    
    #endregion
    
    #region PHP Parser Tests
    
    private readonly PhpErrorParser _phpParser = new();
    
    [Fact]
    public void Php_FatalError_ParsesCorrectly()
    {
        var stderr = @"Fatal error: Uncaught Error: Call to undefined function foo() in /var/www/app/index.php on line 42

Stack trace:
#0 /var/www/app/bootstrap.php(28): main()
#1 {main}";
        
        var result = _phpParser.Parse(stderr);
        
        Assert.True(result.Success);
        Assert.Equal("Fatal error", result.Exception?.Type);
        Assert.Contains("undefined function", result.Exception?.Message);
    }
    
    [Fact]
    public void Php_FatalError_GivesHighConfidence()
    {
        var stderr = "Fatal error: test in /app/index.php on line 10";
        Assert.Equal(ParserConfidence.High, _phpParser.CanParse(stderr));
    }
    
    #endregion
    
    #region Swift Parser Tests
    
    private readonly SwiftErrorParser _swiftParser = new();
    
    [Fact]
    public void Swift_FatalError_ParsesCorrectly()
    {
        var stderr = @"Fatal error: Index out of range: file main.swift, line 42";
        
        var result = _swiftParser.Parse(stderr);
        
        Assert.True(result.Success);
        Assert.Equal("FatalError", result.Exception?.Type);
        Assert.Equal("Index out of range", result.Exception?.Message);
        Assert.Single(result.Frames);
        Assert.Equal("main.swift", result.Frames[0].FilePath);
        Assert.Equal(42, result.Frames[0].LineNumber);
    }
    
    [Fact]
    public void Swift_PreconditionFailed_ParsesCorrectly()
    {
        var stderr = "Precondition failed: Value must be positive: file Utils.swift, line 15";
        
        var result = _swiftParser.Parse(stderr);
        
        Assert.True(result.Success);
        Assert.Equal("PreconditionFailure", result.Exception?.Type);
    }
    
    [Fact]
    public void Swift_FatalError_GivesHighConfidence()
    {
        var stderr = "Fatal error: test: file app.swift, line 1";
        Assert.Equal(ParserConfidence.High, _swiftParser.CanParse(stderr));
    }
    
    #endregion
    
    #region Cross-Language Detection Tests
    
    /// <summary>
    /// Property 1: Language Detection Accuracy
    /// Each parser should correctly identify its own language's errors.
    /// </summary>
    [Fact]
    public void AllParsers_CorrectlyIdentifyOwnLanguage()
    {
        var testCases = new (IErrorParser Parser, string Sample, ParserConfidence MinConfidence)[]
        {
            (_javaParser, "java.lang.Exception: test\n\tat Main.run(Main.java:1)", ParserConfidence.High),
            (_goParser, "panic: test\ngoroutine 1 [running]:", ParserConfidence.High),
            (_rustParser, "thread 'main' panicked at 'test', src/main.rs:1:1", ParserConfidence.High),
            (_rubyParser, "from app.rb:1:in `main'", ParserConfidence.High),
            (_phpParser, "Fatal error: test in /app.php on line 1", ParserConfidence.High),
            (_swiftParser, "Fatal error: test: file app.swift, line 1", ParserConfidence.High),
        };
        
        foreach (var (parser, sample, minConfidence) in testCases)
        {
            var confidence = parser.CanParse(sample);
            Assert.True(confidence >= minConfidence, 
                $"{parser.LanguageName} parser should have at least {minConfidence} confidence for its own errors, got {confidence}");
        }
    }
    
    #endregion
}
