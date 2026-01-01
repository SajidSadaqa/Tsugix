using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Tsugix.AiSurgeon;

/// <summary>
/// Displays fix suggestions as colored diffs using Spectre.Console.
/// </summary>
public sealed class SpectreConsoleDiffView : IDiffView
{
    private readonly IAnsiConsole _console;
    private readonly bool _useColors;
    
    /// <summary>
    /// Creates a new diff view with the default console.
    /// </summary>
    public SpectreConsoleDiffView() : this(AnsiConsole.Console)
    {
    }
    
    /// <summary>
    /// Creates a new diff view with a custom console.
    /// </summary>
    /// <param name="console">The console to write to.</param>
    public SpectreConsoleDiffView(IAnsiConsole console)
    {
        _console = console;
        _useColors = console.Profile.Capabilities.ColorSystem != ColorSystem.NoColors;
    }
    
    /// <inheritdoc />
    public void Display(FixSuggestion fix)
    {
        var panel = CreatePanel(fix);
        _console.Write(panel);
    }
    
    /// <inheritdoc />
    public string Render(FixSuggestion fix)
    {
        var sb = new StringBuilder();
        
        // Header
        sb.AppendLine($"🔧 Suggested Fix for: {fix.FilePath}");
        sb.AppendLine(new string('─', 60));
        sb.AppendLine();
        
        // Diff lines
        var startLine = fix.StartLine ?? 1;
        
        // Show removed lines (original)
        foreach (var (line, index) in fix.OriginalLines.Select((l, i) => (l, i)))
        {
            var lineNum = startLine + index;
            sb.AppendLine($"- {lineNum,4} │ {line}");
        }
        
        // Show added lines (replacement)
        foreach (var (line, index) in fix.ReplacementLines.Select((l, i) => (l, i)))
        {
            var lineNum = startLine + index;
            sb.AppendLine($"+ {lineNum,4} │ {line}");
        }
        
        sb.AppendLine();
        sb.AppendLine(new string('─', 60));
        
        // Explanation
        sb.AppendLine($"💡 Explanation: {fix.Explanation}");
        sb.AppendLine();
        
        // Confidence
        sb.AppendLine($"Confidence: {fix.Confidence}%");
        
        return sb.ToString();
    }
    
    private Panel CreatePanel(FixSuggestion fix)
    {
        var content = new List<IRenderable>();
        
        // Diff content
        var diffTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("").NoWrap());
        
        var startLine = fix.StartLine ?? 1;
        
        // Show removed lines (original)
        foreach (var (line, index) in fix.OriginalLines.Select((l, i) => (l, i)))
        {
            var lineNum = startLine + index;
            var markup = _useColors
                ? new Markup($"[red]- {lineNum,4} │ {EscapeMarkup(line)}[/]")
                : new Markup($"- {lineNum,4} │ {EscapeMarkup(line)}");
            diffTable.AddRow(markup);
        }
        
        // Show added lines (replacement)
        foreach (var (line, index) in fix.ReplacementLines.Select((l, i) => (l, i)))
        {
            var lineNum = startLine + index;
            var markup = _useColors
                ? new Markup($"[green]+ {lineNum,4} │ {EscapeMarkup(line)}[/]")
                : new Markup($"+ {lineNum,4} │ {EscapeMarkup(line)}");
            diffTable.AddRow(markup);
        }
        
        content.Add(diffTable);
        content.Add(new Rule().RuleStyle("dim"));
        
        // Explanation
        content.Add(new Markup($"[bold]💡 Explanation:[/] {EscapeMarkup(fix.Explanation)}"));
        content.Add(Text.Empty);
        
        // Confidence with color coding
        var confidenceColor = fix.Confidence >= 80 ? "green" : fix.Confidence >= 50 ? "yellow" : "red";
        content.Add(new Markup($"[dim]Confidence:[/] [{confidenceColor}]{fix.Confidence}%[/]"));
        
        var rows = new Rows(content);
        
        var panel = new Panel(rows)
        {
            Header = new PanelHeader($"🔧 Suggested Fix for: [bold]{EscapeMarkup(fix.FilePath)}[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 1, 1, 1)
        };
        
        return panel;
    }
    
    private static string EscapeMarkup(string text)
    {
        return Markup.Escape(text);
    }
}
