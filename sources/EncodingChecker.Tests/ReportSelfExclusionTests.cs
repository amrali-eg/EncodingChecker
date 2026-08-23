using System.Collections.Concurrent;
using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// A -Report output path inside the scanned tree must never be picked up as an
/// ordinary scan candidate, even with a broad -Include "*", across repeated runs.
/// </summary>
public sealed class ReportSelfExclusionTests : IDisposable
{
    private readonly string _root;

    public ReportSelfExclusionTests()
    {
        _root = Directory.CreateTempSubdirectory("ec_report_exclusion_").FullName;
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

    [Fact]
    public void ReportInsideScannedTree_IsExcludedFromItsOwnAndLaterScans()
    {
        string sourcePath = Path.Combine(_root, "source.txt");
        File.WriteAllText(sourcePath, TestContent.Multilingual, new UTF8Encoding(false));

        string reportPath = Path.Combine(_root, "report.csv");

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            IncludePatterns = ["*"],
            ExcludedFullPath = reportPath,
            Action = ScanAction.Detect,
        };

        // Run 1: the report doesn't exist yet, but the path is already excluded.
        var firstRun = new List<ConversionReportEntry>();
        ScanEngine.ScanDirectory(options, firstRun.Add, CancellationToken.None);
        Assert.Equal(sourcePath, Assert.Single(firstRun).FilePath);

        File.WriteAllText(reportPath, "File,Encoding,BOM,Target,TargetBOM,Result\r\n", new UTF8Encoding(false));

        // Run 2: the report file now exists in the scanned tree; a broad -Include "*"
        // must still not pick it up, since it's excluded by exact full path.
        var secondRun = new List<ConversionReportEntry>();
        ScanEngine.ScanDirectory(options, secondRun.Add, CancellationToken.None);
        Assert.Equal(sourcePath, Assert.Single(secondRun).FilePath);
    }

    [Fact]
    public void ReportInsideScannedTree_WithRelativeBaseDirectory_IsExcludedFromALaterScan()
    {
        // The bug this guards: excludedFullPath is always absolute, but a relative
        // BaseDirectory (e.g. "-BasePath .") makes enumerated file paths relative too,
        // so a naive string comparison never matches and a later run rescans its own
        // report as ordinary input.
        string sourcePath = Path.Combine(_root, "source.txt");
        File.WriteAllText(sourcePath, TestContent.Multilingual, new UTF8Encoding(false));

        string reportPath = Path.Combine(_root, "report.csv");

        string originalDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = _root;

            var options = new ScanDirectoryOptions
            {
                BaseDirectory = ".",
                IncludePatterns = ["*"],
                ExcludedFullPath = Path.GetFullPath(reportPath),
                Action = ScanAction.Detect,
            };

            var firstRun = new List<ConversionReportEntry>();
            ScanEngine.ScanDirectory(options, firstRun.Add, CancellationToken.None);
            Assert.Single(firstRun);

            File.WriteAllText(reportPath, "File,Encoding,BOM,Target,TargetBOM,Result\r\n", new UTF8Encoding(false));

            var secondRun = new List<ConversionReportEntry>();
            ScanEngine.ScanDirectory(options, secondRun.Add, CancellationToken.None);

            ConversionReportEntry onlyEntry = Assert.Single(secondRun);
            Assert.DoesNotContain("report.csv", onlyEntry.FilePath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }
    }

    [Fact]
    public void ReportOutsideScannedTree_ExcludedFullPathHasNoEffect()
    {
        string sourcePath = Path.Combine(_root, "source.txt");
        File.WriteAllText(sourcePath, TestContent.Multilingual, new UTF8Encoding(false));

        string unrelatedPath = Path.Combine(_root, "not-the-report.csv");
        File.WriteAllText(unrelatedPath, "irrelevant", new UTF8Encoding(false));

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            IncludePatterns = ["*"],
            ExcludedFullPath = Path.Combine(_root, "report.csv"), // never created
            Action = ScanAction.Detect,
        };

        // Two matching files, so onEntry can fire concurrently; List<T>.Add would race.
        var entries = new ConcurrentBag<ConversionReportEntry>();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.FilePath == sourcePath);
        Assert.Contains(entries, e => e.FilePath == unrelatedPath);
    }
}
