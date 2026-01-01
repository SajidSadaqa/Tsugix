using Spectre.Console;

namespace Tsugix.AiSurgeon;

/// <summary>
/// User response to a fix confirmation prompt.
/// </summary>
public enum FixConfirmation
{
    /// <summary>
    /// Apply the fix.
    /// </summary>
    Apply,
    
    /// <summary>
    /// Skip this fix.
    /// </summary>
    Skip,
    
    /// <summary>
    /// Edit the fix (not implemented yet).
    /// </summary>
    Edit
}

/// <summary>
/// Handles user interaction for fix confirmation and re-run prompts.
/// </summary>
public sealed class UserInteraction
{
    private readonly IAnsiConsole _console;
    private readonly bool _autoApply;
    private readonly bool _autoRerun;
    
    /// <summary>
    /// Creates a new user interaction handler.
    /// </summary>
    /// <param name="autoApply">Whether to automatically apply fixes without prompting.</param>
    /// <param name="autoRerun">Whether to automatically re-run commands without prompting.</param>
    public UserInteraction(bool autoApply = false, bool autoRerun = false)
        : this(AnsiConsole.Console, autoApply, autoRerun)
    {
    }
    
    /// <summary>
    /// Creates a new user interaction handler with a custom console.
    /// </summary>
    /// <param name="console">The console to use for prompts.</param>
    /// <param name="autoApply">Whether to automatically apply fixes without prompting.</param>
    /// <param name="autoRerun">Whether to automatically re-run commands without prompting.</param>
    public UserInteraction(IAnsiConsole console, bool autoApply = false, bool autoRerun = false)
    {
        _console = console;
        _autoApply = autoApply;
        _autoRerun = autoRerun;
    }
    
    /// <summary>
    /// Prompts the user to confirm applying a fix.
    /// </summary>
    /// <returns>The user's response.</returns>
    public FixConfirmation PromptFixConfirmation()
    {
        if (_autoApply)
        {
            return FixConfirmation.Apply;
        }
        
        _console.WriteLine();
        var response = _console.Prompt(
            new TextPrompt<string>("Apply this fix? [Y/n/e(dit)]")
                .DefaultValue("y")
                .AllowEmpty());
        
        return response.ToLowerInvariant() switch
        {
            "" or "y" or "yes" => FixConfirmation.Apply,
            "n" or "no" => FixConfirmation.Skip,
            "e" or "edit" => FixConfirmation.Edit,
            _ => FixConfirmation.Skip
        };
    }
    
    /// <summary>
    /// Prompts the user to confirm re-running the command.
    /// </summary>
    /// <returns>True if the user wants to re-run.</returns>
    public bool PromptRerun()
    {
        if (_autoRerun)
        {
            return true;
        }
        
        _console.WriteLine();
        return _console.Confirm("Re-run command?", defaultValue: true);
    }
    
    /// <summary>
    /// Displays a success message after applying a fix.
    /// </summary>
    /// <param name="backupPath">The path to the backup file.</param>
    public void ShowFixApplied(string backupPath)
    {
        _console.MarkupLine("[green]✓ Fix applied successfully![/]");
        if (!string.IsNullOrEmpty(backupPath))
        {
            _console.MarkupLine($"[dim]Backup saved to: {Markup.Escape(backupPath)}[/]");
        }
    }
    
    /// <summary>
    /// Displays a message when a fix is skipped.
    /// </summary>
    public void ShowFixSkipped()
    {
        _console.MarkupLine("[yellow]→ Fix skipped[/]");
    }
    
    /// <summary>
    /// Displays a message when a fix fails.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    public void ShowFixFailed(string errorMessage)
    {
        _console.MarkupLine($"[red]✗ Fix failed: {Markup.Escape(errorMessage)}[/]");
    }
    
    /// <summary>
    /// Displays a summary of applied and skipped fixes.
    /// </summary>
    /// <param name="applied">Number of fixes applied.</param>
    /// <param name="skipped">Number of fixes skipped.</param>
    public void ShowFixSummary(int applied, int skipped)
    {
        _console.WriteLine();
        if (applied > 0)
        {
            _console.MarkupLine($"[green]Applied {applied} fix(es)[/]");
        }
        if (skipped > 0)
        {
            _console.MarkupLine($"[yellow]Skipped {skipped} fix(es)[/]");
        }
    }
    
    /// <summary>
    /// Displays a success message after re-running the command.
    /// </summary>
    public void ShowRerunSuccess()
    {
        _console.MarkupLine("[green]✓ Command completed successfully![/]");
    }
    
    /// <summary>
    /// Displays a message when re-run fails.
    /// </summary>
    /// <param name="exitCode">The exit code of the failed command.</param>
    public void ShowRerunFailed(int exitCode)
    {
        _console.MarkupLine($"[red]✗ Command failed with exit code {exitCode}[/]");
    }
    
    /// <summary>
    /// Prompts the user to analyze a new error after re-run failure.
    /// </summary>
    /// <returns>True if the user wants to analyze the new error.</returns>
    public bool PromptAnalyzeNewError()
    {
        _console.WriteLine();
        return _console.Confirm("Analyze new error?", defaultValue: true);
    }
    
    /// <summary>
    /// Displays a message when AI analysis is skipped.
    /// </summary>
    public void ShowAiSkipped()
    {
        _console.MarkupLine("[dim]AI analysis skipped[/]");
    }
    
    /// <summary>
    /// Displays an error message when AI analysis fails.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    public void ShowAiError(string errorMessage)
    {
        _console.MarkupLine($"[red]AI analysis failed: {Markup.Escape(errorMessage)}[/]");
        _console.MarkupLine("[dim]Showing error context only[/]");
    }
    
    /// <summary>
    /// Displays a message when no API key is configured.
    /// </summary>
    /// <param name="envVarName">The environment variable name for the API key.</param>
    public void ShowMissingApiKey(string envVarName)
    {
        _console.MarkupLine($"[yellow]No API key found. Set {envVarName} environment variable.[/]");
    }
}
