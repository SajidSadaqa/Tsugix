using System.Text;

namespace Tsugix.ContextEngine;

/// <summary>
/// Reads source code files and extracts context windows around error locations.
/// </summary>
public class SourceReader : ISourceReader
{
    private const int DefaultContextLines = 10;
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    
    public SourceSnippet? ReadContext(string filePath, int lineNumber, int windowSize = DefaultContextLines)
    {
        if (string.IsNullOrEmpty(filePath) || lineNumber < 1)
            return null;
        
        try
        {
            // Resolve path
            var resolvedPath = ResolvePath(filePath);
            if (resolvedPath == null || !File.Exists(resolvedPath))
                return null;
            
            // Check file size
            var fileInfo = new FileInfo(resolvedPath);
            if (fileInfo.Length > MaxFileSizeBytes)
                return null;
            
            // Read file with encoding detection
            var lines = ReadFileLines(resolvedPath);
            if (lines == null || lines.Length == 0)
                return null;
            
            // Calculate window
            var (startLine, endLine) = CalculateWindow(lineNumber, lines.Length, windowSize);
            
            // Extract lines
            var sourceLines = new List<SourceLine>();
            for (int i = startLine; i <= endLine; i++)
            {
                sourceLines.Add(new SourceLine
                {
                    LineNumber = i,
                    Content = lines[i - 1], // Convert to 0-based index
                    IsErrorLine = i == lineNumber
                });
            }
            
            return new SourceSnippet
            {
                FilePath = resolvedPath,
                StartLine = startLine,
                EndLine = endLine,
                ErrorLine = lineNumber,
                Lines = sourceLines
            };
        }
        catch
        {
            return null;
        }
    }
    
    public async Task<SourceSnippet?> ReadContextAsync(string filePath, int lineNumber, int windowSize = DefaultContextLines, CancellationToken cancellationToken = default)
    {
        // For now, just wrap the sync method
        return await Task.Run(() => ReadContext(filePath, lineNumber, windowSize), cancellationToken);
    }
    
    private string? ResolvePath(string filePath)
    {
        // Handle absolute paths
        if (Path.IsPathRooted(filePath))
            return filePath;
        
        // Handle relative paths
        var currentDir = Directory.GetCurrentDirectory();
        var resolved = Path.GetFullPath(Path.Combine(currentDir, filePath));
        
        if (File.Exists(resolved))
            return resolved;
        
        // Try the path as-is
        if (File.Exists(filePath))
            return Path.GetFullPath(filePath);
        
        return null;
    }
    
    private string[]? ReadFileLines(string path)
    {
        try
        {
            var encoding = DetectEncoding(path);
            return File.ReadAllLines(path, encoding);
        }
        catch
        {
            return null;
        }
    }
    
    private static Encoding DetectEncoding(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        var bom = new byte[4];
        _ = stream.Read(bom.AsSpan());
        
        if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return Encoding.UTF8;
        if (bom[0] == 0xFF && bom[1] == 0xFE)
            return Encoding.Unicode;
        if (bom[0] == 0xFE && bom[1] == 0xFF)
            return Encoding.BigEndianUnicode;
        if (bom[0] == 0x00 && bom[1] == 0x00 && bom[2] == 0xFE && bom[3] == 0xFF)
            return Encoding.UTF32;
        
        return Encoding.UTF8;
    }
    
    private static (int StartLine, int EndLine) CalculateWindow(int errorLine, int totalLines, int windowSize)
    {
        var halfWindow = windowSize;
        
        var startLine = Math.Max(1, errorLine - halfWindow);
        var endLine = Math.Min(totalLines, errorLine + halfWindow);
        
        if (startLine == 1 && endLine < totalLines)
        {
            endLine = Math.Min(totalLines, 1 + (2 * halfWindow));
        }
        else if (endLine == totalLines && startLine > 1)
        {
            startLine = Math.Max(1, totalLines - (2 * halfWindow));
        }
        
        return (startLine, endLine);
    }
}
