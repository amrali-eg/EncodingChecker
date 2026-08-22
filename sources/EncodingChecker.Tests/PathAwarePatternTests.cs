namespace EncodingChecker.Tests;

/// <summary>
/// -Include/-Exclude patterns: a mask with no separator matches the bare filename at any
/// depth (backward compatible); one containing a separator matches the path relative to
/// the scan root instead, with "/" and "\" treated identically.
/// </summary>
public sealed class PathAwarePatternTests : IDisposable
{
    private readonly string _root;

    public PathAwarePatternTests()
    {
        _root = Directory.CreateTempSubdirectory("ec_path_patterns_").FullName;

        Directory.CreateDirectory(Path.Combine(_root, "src"));
        Directory.CreateDirectory(Path.Combine(_root, "src", "nested"));
        Directory.CreateDirectory(Path.Combine(_root, "other"));

        File.WriteAllText(Path.Combine(_root, "root.cs"), "x");
        File.WriteAllText(Path.Combine(_root, "src", "a.cs"), "x");
        File.WriteAllText(Path.Combine(_root, "src", "nested", "b.cs"), "x");
        File.WriteAllText(Path.Combine(_root, "other", "c.cs"), "x");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private HashSet<string> Scan(string includePattern)
    {
        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            IncludePatterns = [includePattern],
            Action = ScanAction.Detect,
        };

        var entries = new List<ConversionReportEntry>();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        return
        [
            .. entries.Select(e =>
                Path.GetRelativePath(_root, e.FilePath).Replace('\\', '/'))
        ];
    }

    [Fact]
    public void SeparatorFreePattern_StillMatchesAtEveryDepth()
    {
        // Regression guard: this must keep matching every *.cs file regardless of depth,
        // exactly as it did before path-aware matching was introduced.
        HashSet<string> found = Scan("*.cs");

        Assert.Equal(
            new HashSet<string> { "root.cs", "src/a.cs", "src/nested/b.cs", "other/c.cs" },
            found);
    }

    [Fact]
    public void PathQualifiedPattern_MatchesOnlyTheIntendedSubtree()
    {
        HashSet<string> found = Scan("src/*.cs");

        Assert.Equal(new HashSet<string> { "src/a.cs", "src/nested/b.cs" }, found);
        Assert.DoesNotContain("root.cs", found);
        Assert.DoesNotContain("other/c.cs", found);
    }

    [Fact]
    public void PathQualifiedPattern_WithBackslash_MatchesIdenticallyToForwardSlash()
    {
        HashSet<string> withSlash = Scan("src/*.cs");
        HashSet<string> withBackslash = Scan(@"src\*.cs");

        Assert.Equal(withSlash, withBackslash);
    }
}
