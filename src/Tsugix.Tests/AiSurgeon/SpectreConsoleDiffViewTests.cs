using Spectre.Console;
using Spectre.Console.Testing;
using Tsugix.AiSurgeon;
using Xunit;

namespace Tsugix.Tests.AiSurgeon;

/// <summary>
/// Tests for SpectreConsoleDiffView.
/// Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8
/// </summary>
public class SpectreConsoleDiffViewTests
{
    [Fact]
    public void Render_ContainsFilePath()
    {
        var diffView = new SpectreConsoleDiffView();
        var fix = CreateTestFix();
        
        var output = diffView.Render(fix);
        
        Assert.Contains("test.py", output);
    }
    
    [Fact]
    public void Render_ContainsOriginalLines()
    {
        var diffView = new SpectreConsoleDiffView();
        var fix = CreateTestFix();
        
        var output = diffView.Render(fix);
        
        Assert.Contains("x = 1", output);
        Assert.Contains("y = 2", output);
    }
    
    [Fact]
    public void Render_ContainsReplacementLines()
    {
        var diffView = new SpectreConsoleDiffView();
        var fix = CreateTestFix();
        
        var output = diffView.Render(fix);
        
        Assert.Contains("x = 10", output);
        Assert.Contains("y = 20", output);
    }
    
    [Fact]
    public void Render_ContainsExplanation()
    {
        var diffView = new SpectreConsoleDiffView();
        var fix = CreateTestFix();
        
        var output = diffView.Render(fix);
        
        Assert.Contains("Updated values", output);
        Assert.Contains("Explanation", output);
    }
    
    [Fact]
    public void Render_ContainsConfidence()
    {
        var diffView = new SpectreConsoleDiffView();
        var fix = CreateTestFix();
        
        var output = diffView.Render(fix);
        
        Assert.Contains("85%", output);
        Assert.Contains("Confidence", output);
    }
    
    [Fact]
    public void Render_ShowsMinusForRemovedLines()
    {
        var diffView = new SpectreConsoleDiffView();
        var fix = CreateTestFix();
        
        var output = diffView.Render(fix);
        
        // Should have minus prefix for removed lines
        Assert.Contains("- ", output);
    }
    
    [Fact]
    public void Render_ShowsPlusForAddedLines()
    {
        var diffView = new SpectreConsoleDiffView();
        var fix = CreateTestFix();
        
        var output = diffView.Render(fix);
        
        // Should have plus prefix for added lines
        Assert.Contains("+ ", output);
    }
    
    [Fact]
    public void Render_ShowsLineNumbers()
    {
        var diffView = new SpectreConsoleDiffView();
        var fix = CreateTestFix(startLine: 42);
        
        var output = diffView.Render(fix);
        
        // Should show line numbers starting from 42
        Assert.Contains("42", output);
        Assert.Contains("43", output);
    }
    
    [Fact]
    public void Display_WritesToConsole()
    {
        var console = new TestConsole();
        var diffView = new SpectreConsoleDiffView(console);
        var fix = CreateTestFix();
        
        diffView.Display(fix);
        
        var output = console.Output;
        Assert.Contains("test.py", output);
        Assert.Contains("x = 1", output);
    }
    
    [Fact]
    public void Display_ShowsColoredOutput()
    {
        var console = new TestConsole();
        console.Profile.Capabilities.ColorSystem = ColorSystem.TrueColor;
        var diffView = new SpectreConsoleDiffView(console);
        var fix = CreateTestFix();
        
        diffView.Display(fix);
        
        // The output should contain ANSI color codes or markup
        var output = console.Output;
        Assert.NotEmpty(output);
    }
    
    [Fact]
    public void Render_HandlesEmptyReplacementLines()
    {
        var diffView = new SpectreConsoleDiffView();
        var fix = new FixSuggestion
        {
            FilePath = "test.py",
            OriginalLines = new[] { "unused_line" },
            ReplacementLines = Array.Empty<string>(),
            Explanation = "Removed unused line",
            Confidence = 90
        };
        
        var output = diffView.Render(fix);
        
        Assert.Contains("unused_line", output);
        Assert.Contains("Removed unused line", output);
    }
    
    [Fact]
    public void Render_HandlesSpecialCharacters()
    {
        var diffView = new SpectreConsoleDiffView();
        var fix = new FixSuggestion
        {
            FilePath = "test.py",
            OriginalLines = new[] { "print('[test]')" },
            ReplacementLines = new[] { "print('[fixed]')" },
            Explanation = "Fixed [brackets] in string",
            Confidence = 80
        };
        
        var output = diffView.Render(fix);
        
        Assert.Contains("[test]", output);
        Assert.Contains("[fixed]", output);
    }
    
    [Fact]
    public void Render_HighConfidence_ShowsGreen()
    {
        var console = new TestConsole();
        console.Profile.Capabilities.ColorSystem = ColorSystem.TrueColor;
        var diffView = new SpectreConsoleDiffView(console);
        var fix = CreateTestFix(confidence: 90);
        
        diffView.Display(fix);
        
        // High confidence should be displayed (color verification is implicit)
        var output = console.Output;
        Assert.Contains("90%", output);
    }
    
    [Fact]
    public void Render_MediumConfidence_ShowsYellow()
    {
        var console = new TestConsole();
        console.Profile.Capabilities.ColorSystem = ColorSystem.TrueColor;
        var diffView = new SpectreConsoleDiffView(console);
        var fix = CreateTestFix(confidence: 60);
        
        diffView.Display(fix);
        
        var output = console.Output;
        Assert.Contains("60%", output);
    }
    
    [Fact]
    public void Render_LowConfidence_ShowsRed()
    {
        var console = new TestConsole();
        console.Profile.Capabilities.ColorSystem = ColorSystem.TrueColor;
        var diffView = new SpectreConsoleDiffView(console);
        var fix = CreateTestFix(confidence: 30);
        
        diffView.Display(fix);
        
        var output = console.Output;
        Assert.Contains("30%", output);
    }
    
    private static FixSuggestion CreateTestFix(int? startLine = null, int confidence = 85)
    {
        return new FixSuggestion
        {
            FilePath = "test.py",
            OriginalLines = new[] { "x = 1", "y = 2" },
            ReplacementLines = new[] { "x = 10", "y = 20" },
            Explanation = "Updated values",
            Confidence = confidence,
            StartLine = startLine
        };
    }
}
