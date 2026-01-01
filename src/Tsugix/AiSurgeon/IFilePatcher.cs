namespace Tsugix.AiSurgeon;

/// <summary>
/// Options for file patching.
/// </summary>
public sealed record PatchOptions
{
    /// <summary>
    /// Whether to create a backup before patching.
    /// </summary>
    public bool CreateBackup { get; init; } = true;
    
    /// <summary>
    /// Whether to verify the original content matches before patching.
    /// </summary>
    public bool VerifyContent { get; init; } = true;
}

/// <summary>
/// Applies fix suggestions to source files.
/// </summary>
public interface IFilePatcher
{
    /// <summary>
    /// Applies a fix suggestion to the target file.
    /// </summary>
    /// <param name="fix">The fix suggestion to apply.</param>
    /// <param name="options">Options for the patch operation.</param>
    /// <returns>The result of the patch operation.</returns>
    PatchResult Apply(FixSuggestion fix, PatchOptions options);
    
    /// <summary>
    /// Checks if the original content in the fix matches the file content.
    /// </summary>
    /// <param name="fix">The fix suggestion to verify.</param>
    /// <returns>True if the content matches.</returns>
    bool VerifyContent(FixSuggestion fix);
}
