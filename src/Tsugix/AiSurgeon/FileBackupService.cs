namespace Tsugix.AiSurgeon;

/// <summary>
/// Service for creating file backups before patching.
/// Backups are stored in .tsugix/backup/{timestamp}/{relativePath}
/// </summary>
public sealed class FileBackupService
{
    private const string BackupDirectory = ".tsugix/backup";
    private readonly string _rootDirectory;
    
    /// <summary>
    /// Creates a new backup service.
    /// </summary>
    /// <param name="rootDirectory">Root directory for backup storage (null = working directory).</param>
    public FileBackupService(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Environment.CurrentDirectory;
    }
    
    /// <summary>
    /// Creates a backup of the specified file.
    /// </summary>
    /// <param name="filePath">The file to backup.</param>
    /// <returns>The path to the backup file.</returns>
    public string CreateBackup(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Cannot backup non-existent file", filePath);
        }
        
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var relativePath = GetRelativePath(filePath);
        
        // Create backup directory structure: .tsugix/backup/{timestamp}/{relativePath}
        var backupDir = Path.Combine(_rootDirectory, BackupDirectory, timestamp);
        var backupPath = Path.Combine(backupDir, relativePath);
        
        // Ensure backup directory exists
        var backupFileDir = Path.GetDirectoryName(backupPath);
        if (!string.IsNullOrEmpty(backupFileDir))
        {
            Directory.CreateDirectory(backupFileDir);
        }
        
        File.Copy(filePath, backupPath, overwrite: true);
        
        return backupPath;
    }
    
    /// <summary>
    /// Restores a file from its backup.
    /// </summary>
    /// <param name="backupPath">The backup file path.</param>
    /// <param name="originalPath">The original file path to restore to.</param>
    public void RestoreBackup(string backupPath, string originalPath)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("Backup file not found", backupPath);
        }
        
        File.Copy(backupPath, originalPath, overwrite: true);
    }
    
    /// <summary>
    /// Checks if a backup exists for the specified file.
    /// </summary>
    /// <param name="backupPath">The backup file path.</param>
    /// <returns>True if the backup exists.</returns>
    public bool BackupExists(string backupPath)
    {
        return File.Exists(backupPath);
    }
    
    /// <summary>
    /// Lists all backup timestamps.
    /// </summary>
    /// <returns>List of backup timestamps.</returns>
    public IReadOnlyList<string> ListBackups()
    {
        var backupRoot = Path.Combine(_rootDirectory, BackupDirectory);
        if (!Directory.Exists(backupRoot))
        {
            return Array.Empty<string>();
        }
        
        return Directory.GetDirectories(backupRoot)
            .Select(Path.GetFileName)
            .Where(name => name != null)
            .Cast<string>()
            .OrderByDescending(name => name)
            .ToList();
    }
    
    /// <summary>
    /// Gets the relative path of a file from the root directory.
    /// </summary>
    private string GetRelativePath(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var rootFull = Path.GetFullPath(_rootDirectory);
        
        if (fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            var relative = fullPath[(rootFull.Length)..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relative;
        }
        
        // If outside root, use the file name only
        return Path.GetFileName(filePath);
    }
}
