namespace Tsugix.AiSurgeon;

/// <summary>
/// Displays fix suggestions as diffs in the terminal.
/// </summary>
public interface IDiffView
{
    /// <summary>
    /// Renders a fix suggestion as a diff.
    /// </summary>
    /// <param name="fix">The fix suggestion to display.</param>
    void Display(FixSuggestion fix);
    
    /// <summary>
    /// Renders a fix suggestion as a diff and returns the rendered string.
    /// </summary>
    /// <param name="fix">The fix suggestion to display.</param>
    /// <returns>The rendered diff as a string.</returns>
    string Render(FixSuggestion fix);
}
