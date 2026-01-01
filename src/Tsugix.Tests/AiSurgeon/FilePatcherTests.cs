using FsCheck;
using FsCheck.Xunit;
using System.Text;
using Tsugix.AiSurgeon;
using Xunit;

namespace Tsugix.Tests.AiSurgeon;

/// <summary>
/// Tests for FilePatcher.
/// Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7
/// </summary>
public class FilePatcherTests : IDisposable
{
    private readonly string _testDir;
    private readonly FilePatcher _patcher;
    
    public FilePatcherTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"tsugix_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _patcher = new FilePatcher(new FileBackupService(_testDir), _testDir, allowOutsideRoot: false);
    }
    
    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }
    
    [Fact]
    public void Apply_ValidFix_SucceedsAndCreatesBackup()
    {
        var filePath = CreateTestFile("test.py", "x = 1\ny = 2\nz = 3\n");
        var fix = CreateFix("test.py", new[] { "y = 2" }, "y = 20");
        
        var result = _patcher.Apply(fix, new PatchOptions { CreateBackup = true });
        
        Assert.True(result.Success);
        Assert.NotEmpty(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        
        var newContent = File.ReadAllText(filePath);
        Assert.Contains("y = 20", newContent);
    }
    
    [Fact]
    public void Apply_FileNotFound_ReturnsFailed()
    {
        var fix = CreateFix("nonexistent.py", new[] { "x = 1" }, "x = 2");
        
        var result = _patcher.Apply(fix, new PatchOptions());
        
        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage);
    }
    
    [Fact]
    public void Apply_ContentMismatch_ReturnsFailed()
    {
        CreateTestFile("test.py", "x = 1\ny = 2\n");
        var fix = CreateFix("test.py", new[] { "z = 999" }, "z = 1000"); // Not in file
        
        var result = _patcher.Apply(fix, new PatchOptions { VerifyContent = true });
        
        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage);
    }
    
    [Fact]
    public void Apply_PreservesLineEndings_CRLF()
    {
        var content = "x = 1\r\ny = 2\r\nz = 3\r\n";
        var filePath = CreateTestFile("test.py", content);
        var fix = CreateFix("test.py", new[] { "y = 2" }, "y = 20");
        
        _patcher.Apply(fix, new PatchOptions());
        
        var newContent = File.ReadAllText(filePath);
        Assert.Contains("\r\n", newContent);
        Assert.DoesNotContain("\r\n\r\n", newContent); // No double line endings
    }
    
    [Fact]
    public void Apply_PreservesLineEndings_LF()
    {
        var content = "x = 1\ny = 2\nz = 3\n";
        var filePath = CreateTestFile("test.py", content);
        var fix = CreateFix("test.py", new[] { "y = 2" }, "y = 20");
        
        _patcher.Apply(fix, new PatchOptions());
        
        var newContent = File.ReadAllText(filePath);
        Assert.DoesNotContain("\r\n", newContent);
    }
    
    [Fact]
    public void Apply_PreservesEncoding_UTF8WithBOM()
    {
        var content = "x = 1\ny = 2\n";
        var filePath = Path.Combine(_testDir, "test_bom.py");
        File.WriteAllText(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        
        var fix = CreateFix("test_bom.py", new[] { "y = 2" }, "y = 20");
        
        _patcher.Apply(fix, new PatchOptions());
        
        var bytes = File.ReadAllBytes(filePath);
        // UTF-8 BOM: EF BB BF
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }
    
    [Fact]
    public void Apply_PathTraversal_ReturnsFailed()
    {
        CreateTestFile("test.py", "x = 1\n");
        var fix = CreateFix("../../../etc/passwd", new[] { "x" }, "y");
        
        var result = _patcher.Apply(fix, new PatchOptions());
        
        Assert.False(result.Success);
        Assert.Contains("outside", result.ErrorMessage?.ToLower() ?? "");
    }
    
    [Fact]
    public void Apply_AbsolutePathOutsideRoot_ReturnsFailed()
    {
        CreateTestFile("test.py", "x = 1\n");
        var outsidePath = Path.Combine(Path.GetTempPath(), "outside.py");
        var fix = CreateFix(outsidePath, new[] { "x" }, "y");
        
        var result = _patcher.Apply(fix, new PatchOptions());
        
        Assert.False(result.Success);
        Assert.Contains("outside", result.ErrorMessage?.ToLower() ?? "");
    }
    
    [Fact]
    public void Apply_AllowOutsideRoot_Succeeds()
    {
        var outsideDir = Path.Combine(Path.GetTempPath(), $"tsugix_outside_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDir);
        
        try
        {
            var outsidePath = Path.Combine(outsideDir, "outside.py");
            File.WriteAllText(outsidePath, "x = 1\ny = 2\n");
            
            var patcher = new FilePatcher(new FileBackupService(_testDir), _testDir, allowOutsideRoot: true);
            var fix = CreateFix(outsidePath, new[] { "y = 2" }, "y = 20");
            
            var result = patcher.Apply(fix, new PatchOptions { CreateBackup = false });
            
            Assert.True(result.Success);
        }
        finally
        {
            Directory.Delete(outsideDir, recursive: true);
        }
    }
    
    [Fact]
    public void Apply_AtomicWrite_NoPartialWrites()
    {
        var filePath = CreateTestFile("test.py", "original content\n");
        var fix = CreateFix("test.py", new[] { "original content" }, "new content");
        
        var result = _patcher.Apply(fix, new PatchOptions());
        
        Assert.True(result.Success);
        
        // Verify no temp files left behind
        var tempFiles = Directory.GetFiles(_testDir, ".tsugix.tmp.*");
        Assert.Empty(tempFiles);
    }
    
    [Fact]
    public void VerifyContent_MatchingContent_ReturnsTrue()
    {
        CreateTestFile("test.py", "x = 1\ny = 2\nz = 3\n");
        var fix = CreateFix("test.py", new[] { "y = 2" }, "y = 20");
        
        var result = _patcher.VerifyContent(fix);
        
        Assert.True(result);
    }
    
    [Fact]
    public void VerifyContent_NonMatchingContent_ReturnsFalse()
    {
        CreateTestFile("test.py", "x = 1\ny = 2\nz = 3\n");
        var fix = CreateFix("test.py", new[] { "not in file" }, "replacement");
        
        var result = _patcher.VerifyContent(fix);
        
        Assert.False(result);
    }
    
    private string CreateTestFile(string name, string content)
    {
        var path = Path.Combine(_testDir, name);
        File.WriteAllText(path, content);
        return path;
    }
    
    private static FixSuggestion CreateFix(string filePath, string[] originalLines, string replacement)
    {
        return new FixSuggestion
        {
            Edits = new[]
            {
                new FixEdit
                {
                    FilePath = filePath,
                    StartLine = 1,
                    EndLine = originalLines.Length,
                    OriginalLines = originalLines,
                    Replacement = replacement
                }
            },
            Explanation = "Test fix",
            Confidence = 80
        };
    }
}

/// <summary>
/// Property-based tests for FilePatcher.
/// Validates: Requirements 6.1, 6.3, 6.4, 6.7
/// </summary>
public class FilePatcherPropertyTests : IDisposable
{
    private readonly string _testDir;
    
    public FilePatcherPropertyTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"tsugix_prop_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }
    
    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }
    
    /// <summary>
    /// Property 5: Safe Patching Invariants
    /// For any valid fix suggestion applied to a file:
    /// (a) A backup SHALL be created if requested
    /// (b) The original encoding SHALL be preserved
    /// (c) The original line endings SHALL be preserved
    /// (d) The backup SHALL contain the original content
    /// Validates: Requirements 6.1, 6.3, 6.4, 6.7
    /// </summary>
    [Property(MaxTest = 30)]
    public Property SafePatchingInvariants()
    {
        var lineEndingGen = Gen.Elements("\n", "\r\n");
        var contentGen = Gen.Elements("x = 1", "y = 2", "z = 3", "return value");
        
        var testGen = from lineEnding in lineEndingGen
                      from line1 in contentGen
                      from line2 in contentGen
                      from line3 in contentGen
                      select (lineEnding, new[] { line1, line2, line3 });
        
        return Prop.ForAll(
            testGen.ToArbitrary(),
            tuple =>
            {
                var (lineEnding, lines) = tuple;
                var content = string.Join(lineEnding, lines) + lineEnding;
                
                var fileName = $"test_{Guid.NewGuid():N}.py";
                var filePath = Path.Combine(_testDir, fileName);
                File.WriteAllText(filePath, content);
                
                var patcher = new FilePatcher(new FileBackupService(_testDir), _testDir);
                var fix = new FixSuggestion
                {
                    Edits = new[]
                    {
                        new FixEdit
                        {
                            FilePath = fileName,
                            StartLine = 2,
                            EndLine = 2,
                            OriginalLines = new[] { lines[1] },
                            Replacement = "modified = True"
                        }
                    },
                    Confidence = 80
                };
                
                var result = patcher.Apply(fix, new PatchOptions { CreateBackup = true });
                
                if (!result.Success)
                    return false;
                
                // (a) Backup created
                if (!File.Exists(result.BackupPath))
                    return false;
                
                // (b) & (c) Line endings preserved
                var newContent = File.ReadAllText(filePath);
                var hasCorrectLineEnding = lineEnding == "\r\n" 
                    ? newContent.Contains("\r\n") 
                    : !newContent.Contains("\r\n");
                
                // (d) Backup contains original
                var backupContent = File.ReadAllText(result.BackupPath);
                var backupMatchesOriginal = backupContent == content;
                
                return hasCorrectLineEnding && backupMatchesOriginal;
            });
    }
    
    /// <summary>
    /// Property 6: Content Matching Accuracy
    /// For any file content and search pattern, the content matching algorithm
    /// SHALL correctly identify whether the pattern exists in the file.
    /// Validates: Requirements 6.2, 6.5, 6.6
    /// </summary>
    [Property(MaxTest = 50)]
    public Property ContentMatchingAccuracy()
    {
        var lineGen = Gen.Elements("x = 1", "y = 2", "z = 3", "return value", "if condition:");
        
        var testGen = from lines in Gen.ListOf(5, lineGen)
                      from searchIndex in Gen.Choose(0, 4)
                      select (lines.ToArray(), searchIndex);
        
        return Prop.ForAll(
            testGen.ToArbitrary(),
            tuple =>
            {
                var (lines, searchIndex) = tuple;
                var content = string.Join("\n", lines);
                
                var fileName = $"match_{Guid.NewGuid():N}.py";
                var filePath = Path.Combine(_testDir, fileName);
                File.WriteAllText(filePath, content);
                
                var patcher = new FilePatcher(new FileBackupService(_testDir), _testDir);
                
                // Test with content that exists
                var existingFix = new FixSuggestion
                {
                    Edits = new[]
                    {
                        new FixEdit
                        {
                            FilePath = fileName,
                            StartLine = searchIndex + 1,
                            EndLine = searchIndex + 1,
                            OriginalLines = new[] { lines[searchIndex] },
                            Replacement = "modified"
                        }
                    },
                    Confidence = 80
                };
                
                var existsResult = patcher.VerifyContent(existingFix);
                
                // Test with content that doesn't exist
                var nonExistingFix = new FixSuggestion
                {
                    Edits = new[]
                    {
                        new FixEdit
                        {
                            FilePath = fileName,
                            StartLine = 1,
                            EndLine = 1,
                            OriginalLines = new[] { "this_line_does_not_exist_12345" },
                            Replacement = "modified"
                        }
                    },
                    Confidence = 80
                };
                
                var notExistsResult = patcher.VerifyContent(nonExistingFix);
                
                return existsResult && !notExistsResult;
            });
    }
}
