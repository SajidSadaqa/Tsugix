using System.Security.Cryptography;
using System.Text;

namespace Tsugix.AiSurgeon;

/// <summary>
/// Applies fix suggestions to source files with backup, atomic writes, and content verification.
/// </summary>
public sealed class FilePatcher : IFilePatcher
{
    private readonly FileBackupService _backupService;
    private readonly string? _rootDirectory;
    private readonly bool _allowOutsideRoot;
    
    /// <summary>
    /// Creates a new file patcher.
    /// </summary>
    public FilePatcher() : this(new FileBackupService(), null, false)
    {
    }
    
    /// <summary>
    /// Creates a new file patcher with path safety options.
    /// </summary>
    /// <param name="backupService">The backup service to use.</param>
    /// <param name="rootDirectory">Root directory for file operations (null = working directory).</param>
    /// <param name="allowOutsideRoot">Whether to allow patching files outside the root.</param>
    public FilePatcher(FileBackupService backupService, string? rootDirectory = null, bool allowOutsideRoot = false)
    {
        _backupService = backupService;
        _rootDirectory = rootDirectory ?? Environment.CurrentDirectory;
        _allowOutsideRoot = allowOutsideRoot;
    }
    
    /// <inheritdoc />
    public PatchResult Apply(FixSuggestion fix, PatchOptions options)
    {
        // Normalize the fix to ensure we have edits
        fix = fix.Normalize();
        
        if (fix.Edits == null || fix.Edits.Count == 0)
        {
            return PatchResult.Failed("No edits to apply");
        }
        
        // For now, only support single-file edits
        var edit = fix.Edits[0];
        var filePath = edit.FilePath;
        
        // Path safety check
        var pathValidation = ValidatePath(filePath);
        if (!pathValidation.IsValid)
        {
            return PatchResult.Failed(pathValidation.Error!);
        }
        
        var resolvedPath = pathValidation.ResolvedPath!;
        
        if (!File.Exists(resolvedPath))
        {
            return PatchResult.Failed($"File not found: {filePath}");
        }
        
        string backupPath = string.Empty;
        
        try
        {
            // Read file with metadata
            var (content, encoding, lineEnding, originalHash) = ReadFileWithMetadata(resolvedPath);
            var lines = SplitLines(content, lineEnding);
            
            // Verify content matches if requested
            if (options.VerifyContent && edit.OriginalLines != null)
            {
                var matchIndex = FindMatchingBlock(lines, edit.OriginalLines);
                if (matchIndex < 0)
                {
                    return PatchResult.Failed(
                        "Original code not found in file - file may have been modified since the error occurred.");
                }
            }
            
            // Check for stale edits (file changed since we read it)
            if (options.VerifyContent)
            {
                var currentHash = ComputeFileHash(resolvedPath);
                if (currentHash != originalHash)
                {
                    return PatchResult.Failed(
                        "File has been modified since it was read. Use --force to override.");
                }
            }
            
            // Create backup if requested
            if (options.CreateBackup)
            {
                backupPath = _backupService.CreateBackup(resolvedPath);
            }
            
            // Apply the patch
            var newContent = ApplyPatch(lines, edit, lineEnding);
            
            // Atomic write: write to temp file, then replace
            AtomicWrite(resolvedPath, newContent, encoding);
            
            return PatchResult.Succeeded(backupPath);
        }
        catch (Exception ex)
        {
            return PatchResult.Failed(ex.Message, backupPath);
        }
    }
    
    /// <inheritdoc />
    public bool VerifyContent(FixSuggestion fix)
    {
        fix = fix.Normalize();
        
        if (fix.Edits == null || fix.Edits.Count == 0)
            return false;
        
        var edit = fix.Edits[0];
        
        var pathValidation = ValidatePath(edit.FilePath);
        if (!pathValidation.IsValid)
            return false;
        
        if (!File.Exists(pathValidation.ResolvedPath))
            return false;
        
        try
        {
            var content = File.ReadAllText(pathValidation.ResolvedPath!);
            var lineEnding = DetectLineEnding(content);
            var lines = SplitLines(content, lineEnding);
            
            return edit.OriginalLines != null && FindMatchingBlock(lines, edit.OriginalLines) >= 0;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Validates a file path for safety.
    /// </summary>
    private PathValidationResult ValidatePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return PathValidationResult.Invalid("File path is empty");
        }
        
        // Normalize the path
        string resolvedPath;
        try
        {
            // Resolve relative to root directory
            if (Path.IsPathRooted(filePath))
            {
                resolvedPath = Path.GetFullPath(filePath);
            }
            else
            {
                resolvedPath = Path.GetFullPath(Path.Combine(_rootDirectory!, filePath));
            }
        }
        catch (Exception ex)
        {
            return PathValidationResult.Invalid($"Invalid path: {ex.Message}");
        }
        
        // Check for path traversal
        if (filePath.Contains(".."))
        {
            // Verify the resolved path is still under root
            var rootFull = Path.GetFullPath(_rootDirectory!);
            if (!resolvedPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                if (!_allowOutsideRoot)
                {
                    return PathValidationResult.Invalid(
                        $"Path traversal detected: {filePath} resolves outside root directory. Use --allow-outside-root to override.");
                }
            }
        }
        
        // Check if path is under root (unless explicitly allowed)
        if (!_allowOutsideRoot)
        {
            var rootFull = Path.GetFullPath(_rootDirectory!);
            if (!resolvedPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                return PathValidationResult.Invalid(
                    $"File {filePath} is outside the root directory. Use --allow-outside-root to override.");
            }
        }
        
        return PathValidationResult.Valid(resolvedPath);
    }
    
    private static (string Content, Encoding Encoding, string LineEnding, string Hash) ReadFileWithMetadata(string filePath)
    {
        // Read raw bytes to detect encoding
        var bytes = File.ReadAllBytes(filePath);
        var encoding = DetectEncoding(bytes);
        var content = encoding.GetString(bytes);
        
        // Remove BOM if present
        if (content.Length > 0 && content[0] == '\uFEFF')
        {
            content = content[1..];
        }
        
        var lineEnding = DetectLineEnding(content);
        var hash = ComputeHash(bytes);
        
        return (content, encoding, lineEnding, hash);
    }
    
    private static string ComputeFileHash(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        return ComputeHash(bytes);
    }
    
    private static string ComputeHash(byte[] bytes)
    {
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes);
    }
    
    private static Encoding DetectEncoding(byte[] bytes)
    {
        // Check for BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8;
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode; // UTF-16 LE
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode; // UTF-16 BE
        }
        
        // Default to UTF-8 without BOM
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }
    
    private static string DetectLineEnding(string content)
    {
        var crlfCount = 0;
        var lfCount = 0;
        
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
            {
                crlfCount++;
                i++; // Skip the \n
            }
            else if (content[i] == '\n')
            {
                lfCount++;
            }
        }
        
        // Use CRLF if it's the majority, otherwise LF
        return crlfCount >= lfCount ? "\r\n" : "\n";
    }
    
    private static List<string> SplitLines(string content, string lineEnding)
    {
        // Normalize line endings for splitting
        var normalized = content.Replace("\r\n", "\n").Replace("\r", "\n");
        return normalized.Split('\n').ToList();
    }
    
    private static int FindMatchingBlock(List<string> fileLines, IReadOnlyList<string> searchLines)
    {
        if (searchLines.Count == 0)
        {
            return -1;
        }
        
        for (var i = 0; i <= fileLines.Count - searchLines.Count; i++)
        {
            var match = true;
            for (var j = 0; j < searchLines.Count; j++)
            {
                // Trim for comparison to handle whitespace differences
                if (fileLines[i + j].Trim() != searchLines[j].Trim())
                {
                    match = false;
                    break;
                }
            }
            
            if (match)
            {
                return i;
            }
        }
        
        return -1;
    }
    
    private static string ApplyPatch(List<string> lines, FixEdit edit, string lineEnding)
    {
        if (edit.OriginalLines == null)
        {
            throw new InvalidOperationException("Original lines are required for patching");
        }
        
        var matchIndex = FindMatchingBlock(lines, edit.OriginalLines);
        
        if (matchIndex < 0)
        {
            throw new InvalidOperationException("Original content not found in file");
        }
        
        // Build new content
        var newLines = new List<string>();
        
        // Add lines before the match
        for (var i = 0; i < matchIndex; i++)
        {
            newLines.Add(lines[i]);
        }
        
        // Add replacement lines (split by newline)
        if (!string.IsNullOrEmpty(edit.Replacement))
        {
            var replacementLines = edit.Replacement.Split('\n');
            newLines.AddRange(replacementLines);
        }
        
        // Add lines after the match
        for (var i = matchIndex + edit.OriginalLines.Count; i < lines.Count; i++)
        {
            newLines.Add(lines[i]);
        }
        
        return string.Join(lineEnding, newLines);
    }
    
    /// <summary>
    /// Performs an atomic write: write to temp file, flush, then replace original.
    /// </summary>
    private static void AtomicWrite(string targetPath, string content, Encoding encoding)
    {
        var directory = Path.GetDirectoryName(targetPath) ?? ".";
        var tempPath = Path.Combine(directory, $".tsugix.tmp.{Guid.NewGuid():N}");
        
        try
        {
            // Write to temp file
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, encoding))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true); // fsync
            }
            
            // Replace original with temp (atomic on most file systems)
            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            // Clean up temp file if it still exists
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore */ }
            }
        }
    }
    
    private sealed record PathValidationResult
    {
        public bool IsValid { get; init; }
        public string? ResolvedPath { get; init; }
        public string? Error { get; init; }
        
        public static PathValidationResult Valid(string resolvedPath) => new()
        {
            IsValid = true,
            ResolvedPath = resolvedPath
        };
        
        public static PathValidationResult Invalid(string error) => new()
        {
            IsValid = false,
            Error = error
        };
    }
}
