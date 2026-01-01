namespace Tsugix.AiSurgeon;

/// <summary>
/// Result of a file patch operation.
/// </summary>
public sealed record PatchResult
{
    /// <summary>
    /// Whether the patch was successful.
    /// </summary>
    public required bool Success { get; init; }
    
    /// <summary>
    /// Path to the backup file.
    /// </summary>
    public required string BackupPath { get; init; }
    
    /// <summary>
    /// Error message (if failed).
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static PatchResult Succeeded(string backupPath) => new()
    {
        Success = true,
        BackupPath = backupPath
    };
    
    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static PatchResult Failed(string errorMessage, string backupPath = "") => new()
    {
        Success = false,
        BackupPath = backupPath,
        ErrorMessage = errorMessage
    };
}
