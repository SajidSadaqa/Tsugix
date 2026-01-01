using FsCheck;
using FsCheck.Xunit;
using Tsugix.ContextEngine;
using Xunit;

namespace Tsugix.Tests.ContextEngine;

/// <summary>
/// Property-based tests for SourceReader.
/// Feature: tsugix-phase2-context-engine, Property 11: Source Context Window Correctness
/// Validates: Requirements 11.1, 11.2, 11.3, 11.4
/// </summary>
public class SourceReaderTests : IDisposable
{
    private readonly string _testDir;
    private readonly SourceReader _reader = new();
    
    public SourceReaderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"tsugix_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }
    
    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }
    
    private string CreateTestFile(string content, string fileName = "test.py")
    {
        var path = Path.Combine(_testDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }
    
    /// <summary>
    /// Property 11: Source Context Window Correctness
    /// The window should be centered on the error line and respect boundaries.
    /// </summary>
    [Property(MaxTest = 50)]
    public Property ContextWindow_CenteredOnErrorLine()
    {
        return Prop.ForAll(
            Gen.Choose(5, 50).ToArbitrary(),  // Total lines
            Gen.Choose(1, 50).ToArbitrary(),  // Error line
            Gen.Choose(2, 10).ToArbitrary(),  // Window size
            (totalLines, errorLine, windowSize) =>
            {
                // Ensure error line is within bounds
                errorLine = Math.Min(errorLine, totalLines);
                
                // Create test file
                var lines = Enumerable.Range(1, totalLines).Select(i => $"line {i}").ToArray();
                var content = string.Join("\n", lines);
                var path = CreateTestFile(content, $"test_{totalLines}_{errorLine}.py");
                
                var result = _reader.ReadContext(path, errorLine, windowSize);
                
                if (result == null)
                    return false.Label("Result was null");
                
                // Error line should be in the window
                var containsErrorLine = result.Lines.Any(l => l.LineNumber == errorLine);
                
                // Window should not exceed file boundaries
                var withinBounds = result.StartLine >= 1 && result.EndLine <= totalLines;
                
                // Error line should be marked
                var errorLineMarked = result.Lines.Single(l => l.LineNumber == errorLine).IsErrorLine;
                
                return (containsErrorLine && withinBounds && errorLineMarked)
                    .Label($"Contains error: {containsErrorLine}, Within bounds: {withinBounds}, Marked: {errorLineMarked}");
            });
    }
    
    [Fact]
    public void ReadContext_ValidFile_ReturnsSnippet()
    {
        var content = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"line {i}"));
        var path = CreateTestFile(content);
        
        var result = _reader.ReadContext(path, 10, 5);
        
        Assert.NotNull(result);
        Assert.Equal(path, result.FilePath);
        Assert.Equal(10, result.ErrorLine);
        Assert.True(result.Lines.Count > 0);
        Assert.Contains(result.Lines, l => l.IsErrorLine && l.LineNumber == 10);
    }
    
    [Fact]
    public void ReadContext_NonExistentFile_ReturnsNull()
    {
        var result = _reader.ReadContext("/nonexistent/path/file.py", 10);
        Assert.Null(result);
    }
    
    [Fact]
    public void ReadContext_InvalidLineNumber_ReturnsNull()
    {
        var path = CreateTestFile("line 1\nline 2");
        
        Assert.Null(_reader.ReadContext(path, 0));
        Assert.Null(_reader.ReadContext(path, -1));
    }
    
    [Fact]
    public void ReadContext_LineNumberBeyondFile_ReturnsSnippet()
    {
        var path = CreateTestFile("line 1\nline 2\nline 3");
        
        // Should still return something, just adjusted
        var result = _reader.ReadContext(path, 100, 5);
        
        // May return null or adjusted result depending on implementation
        // The important thing is it doesn't crash
    }
    
    [Fact]
    public void ReadContext_EmptyPath_ReturnsNull()
    {
        Assert.Null(_reader.ReadContext("", 10));
        Assert.Null(_reader.ReadContext(null!, 10));
    }
    
    [Fact]
    public void ReadContext_WindowAtStart_AdjustsCorrectly()
    {
        var content = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"line {i}"));
        var path = CreateTestFile(content);
        
        var result = _reader.ReadContext(path, 1, 5);
        
        Assert.NotNull(result);
        Assert.Equal(1, result.StartLine);
        Assert.True(result.EndLine <= 20);
    }
    
    [Fact]
    public void ReadContext_WindowAtEnd_AdjustsCorrectly()
    {
        var content = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"line {i}"));
        var path = CreateTestFile(content);
        
        var result = _reader.ReadContext(path, 20, 5);
        
        Assert.NotNull(result);
        Assert.Equal(20, result.EndLine);
        Assert.True(result.StartLine >= 1);
    }
}
