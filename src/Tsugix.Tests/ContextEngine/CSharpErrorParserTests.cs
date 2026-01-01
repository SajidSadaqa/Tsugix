using FsCheck;
using FsCheck.Xunit;
using Tsugix.ContextEngine;
using Tsugix.ContextEngine.Parsers;
using Xunit;

namespace Tsugix.Tests.ContextEngine;

/// <summary>
/// Property-based tests for CSharpErrorParser.
/// Feature: tsugix-phase2-context-engine, Property 4: C# Parsing Round-Trip
/// Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.6
/// </summary>
public class CSharpErrorParserTests
{
    private readonly CSharpErrorParser _parser = new();
    
    private static Gen<string> GenCSharpFilePath()
    {
        return Gen.Elements(
            "C:\\Users\\dev\\project\\Program.cs",
            "/home/user/project/Service.cs",
            "D:\\src\\MyApp\\Controllers\\HomeController.cs",
            "./src/Utils.cs"
        );
    }
    
    private static Gen<string> GenClassName()
    {
        return Gen.Elements(
            "MyApp.Program",
            "MyApp.Services.UserService",
            "Controllers.HomeController",
            "Utils.Helper"
        );
    }
    
    private static Gen<string> GenMethodName()
    {
        return Gen.Elements(
            "Main",
            "ProcessData",
            "GetUser",
            "HandleRequest"
        );
    }
    
    private static Gen<string> GenExceptionType()
    {
        return Gen.Elements(
            "System.NullReferenceException",
            "System.ArgumentException",
            "System.InvalidOperationException",
            "System.IO.FileNotFoundException",
            "System.IndexOutOfRangeException"
        );
    }
    
    private static Gen<string> GenExceptionMessage()
    {
        return Gen.Elements(
            "Object reference not set to an instance of an object.",
            "Value cannot be null.",
            "Sequence contains no elements.",
            "Could not find file 'test.txt'.",
            "Index was outside the bounds of the array."
        );
    }

    private static Gen<(string Error, string Type, string Message, List<(string Path, int Line, string Class, string Method)> Frames)> GenCSharpError()
    {
        return from frameCount in Gen.Choose(1, 4)
               from frames in Gen.ListOf(frameCount,
                   from path in GenCSharpFilePath()
                   from line in Gen.Choose(1, 500)
                   from cls in GenClassName()
                   from method in GenMethodName()
                   select (path, line, cls, method))
               from errType in GenExceptionType()
               from message in GenExceptionMessage()
               let error = BuildCSharpError(frames.ToList(), errType, message)
               select (error, errType, message, frames.ToList());
    }
    
    private static string BuildCSharpError(List<(string Path, int Line, string Class, string Method)> frames, string errType, string message)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{errType}: {message}");
        
        foreach (var (path, line, cls, method) in frames)
        {
            sb.AppendLine($"   at {cls}.{method}() in {path}:line {line}");
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Property 4: C# Parsing Round-Trip
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CSharpError_ExtractsAllFrames()
    {
        return Prop.ForAll(
            GenCSharpError().ToArbitrary(),
            data =>
            {
                var (error, expectedType, expectedMessage, expectedFrames) = data;
                
                var confidence = _parser.CanParse(error);
                var result = _parser.Parse(error);
                
                var hasHighConfidence = confidence == ParserConfidence.High;
                var parseSuccess = result.Success;
                var correctType = result.Exception?.Type == expectedType;
                var correctMessage = result.Exception?.Message == expectedMessage;
                var correctFrameCount = result.Frames.Count == expectedFrames.Count;
                
                var framesMatch = result.Frames.Count == expectedFrames.Count &&
                    result.Frames.Zip(expectedFrames, (actual, expected) =>
                        actual.FilePath == expected.Path &&
                        actual.LineNumber == expected.Line &&
                        actual.FunctionName == expected.Method)
                    .All(x => x);
                
                return hasHighConfidence && parseSuccess && correctType && 
                       correctMessage && correctFrameCount && framesMatch;
            });
    }
    
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyInput_ReturnsNoneConfidence(string? input)
    {
        Assert.Equal(ParserConfidence.None, _parser.CanParse(input!));
    }
    
    [Fact]
    public void DotNetStackFrame_GivesHighConfidence()
    {
        var stderr = "System.Exception: test\n   at MyApp.Program.Main() in C:\\src\\Program.cs:line 10";
        Assert.Equal(ParserConfidence.High, _parser.CanParse(stderr));
    }
    
    [Theory]
    [InlineData("C:\\Program Files\\dotnet\\shared\\Microsoft.NETCore.App\\6.0.0\\System.Private.CoreLib.dll")]
    [InlineData("/usr/share/dotnet/shared/Microsoft.NETCore.App/6.0.0/System.Private.CoreLib.dll")]
    public void FrameworkPaths_MarkedAsNonUserCode(string libPath)
    {
        var stderr = $"System.Exception: test\n   at System.String.Concat() in {libPath}:line 10";
        var result = _parser.Parse(stderr);
        
        Assert.True(result.Success);
        Assert.Single(result.Frames);
        Assert.False(result.Frames[0].IsUserCode);
    }
    
    [Fact]
    public void RealWorldException_ParsesCorrectly()
    {
        var stderr = @"System.NullReferenceException: Object reference not set to an instance of an object.
   at MyApp.Services.UserService.GetUser(Int32 id) in C:\src\MyApp\Services\UserService.cs:line 42
   at MyApp.Controllers.UserController.Get(Int32 id) in C:\src\MyApp\Controllers\UserController.cs:line 28";
        
        var result = _parser.Parse(stderr);
        
        Assert.True(result.Success);
        Assert.Equal("System.NullReferenceException", result.Exception?.Type);
        Assert.Equal("Object reference not set to an instance of an object.", result.Exception?.Message);
        Assert.Equal(2, result.Frames.Count);
        
        Assert.Equal("C:\\src\\MyApp\\Services\\UserService.cs", result.Frames[0].FilePath);
        Assert.Equal(42, result.Frames[0].LineNumber);
        Assert.Equal("GetUser", result.Frames[0].FunctionName);
        Assert.Equal("MyApp.Services.UserService", result.Frames[0].ClassName);
    }
}
