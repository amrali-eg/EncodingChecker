using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace EncodingChecker.Tests;

/// <summary>
/// A folder EC could not read must leave a trace, the way an unreadable file does.
/// </summary>
/// <remarks>
/// An unreadable file becomes a row with <c>ScanFailed</c> and drives exit code 3. An
/// unreadable directory produced a warning on stderr and nothing else: no row, no counter,
/// exit 0 — and nothing whatever in the GUI, which passes no warning callback. Measured
/// against a denied folder: <c>-Validate -FailOnChanges</c> reported "1 file(s) processed"
/// and exited 0, and a scan whose entire base directory was unreadable printed a
/// header-only CSV and exited 0. A run that examined none of the tree could report success,
/// which is the one thing the coverage report exists to prevent.
/// <para>
/// The exit code is deliberately unchanged; <c>docs/CLI.md</c> states that coverage counts
/// do not affect it, and now also states what that means for a script.
/// </para>
/// </remarks>
public sealed class UnreadableFolderCoverageTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec-unreadable-").FullName;

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

    private static (List<string> Files, DirectoryTraversal.TraversalCounters Counters)
        Walk(string baseDirectory)
    {
        var counters = new DirectoryTraversal.TraversalCounters();

        List<string> files =
        [
            .. DirectoryTraversal.EnumerateFiles(
                baseDirectory,
                includeSubdirectories: true,
                DirectoryTraversal.CompilePatterns(["*"], defaultToMatchAll: true),
                [],
                excludedFullPaths: null,
                onWarning: null,
                counters: counters)
        ];

        return (files, counters);
    }

    [Fact]
    public void AnUnlistableBaseDirectoryIsCounted()
    {
        // Never created, so listing it fails with DirectoryNotFoundException. This is the
        // shape that used to print a header-only CSV and exit 0 with nothing else said.
        string ghost = Path.Combine(_root, "never-created-" + Guid.NewGuid().ToString("N"));

        var (files, counters) = Walk(ghost);

        Assert.Empty(files);
        Assert.Equal(1, counters.DirectoriesUnreadable);
    }

    [Fact]
    public void AnUnreadableFolderIsSaidOutLoudInTheCoverageText()
    {
        // The GUI has no other channel: it passes no warning callback, so this string is
        // the whole of what a window user is told.
        var counters = new DirectoryTraversal.TraversalCounters();
        counters.CountDirectoryUnreadable();

        Assert.Equal("1 folder(s) could not be read", MainForm.FormatCoverage(counters));
    }

    [Fact]
    public void ItIsCountedApartFromFoldersSkippedOnPurpose()
    {
        // Three different reasons a folder was not walked, and the report keeps them apart:
        // two are policy, one is a failure.
        var counters = new DirectoryTraversal.TraversalCounters();
        counters.CountDirectoryExcludedByAttribute();
        counters.CountDirectoryExcludedByName();
        counters.CountDirectoryUnreadable();

        string coverage = MainForm.FormatCoverage(counters);

        Assert.Contains("1 folder(s) not entered", coverage);
        Assert.Contains("1 build/metadata folder(s) not entered", coverage);
        Assert.Contains("1 folder(s) could not be read", coverage);
    }

    [Fact]
    public void AnUnreadableFileStillBecomesARowInstead()
    {
        // The contrast that made the gap visible: files fail loudly, folders did not.
        string path = Path.Combine(_root, "held.txt");
        File.WriteAllText(path, "content", new UTF8Encoding(false));

        using FileStream exclusive = new(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var entries = new EntrySink();
        var counters = new DirectoryTraversal.TraversalCounters();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                Action = ScanAction.Detect,
                Counters = counters,
                MaxParallelism = 1,
            },
            entries.Add,
            CancellationToken.None);

        ConversionReportEntry row = entries.Single();

        Assert.Equal(ConversionRowResult.Error, row.Result);
        Assert.Equal(ConversionReasonCodes.ScanFailed, row.ReasonCode);
        Assert.Equal(0, counters.DirectoriesUnreadable);
    }

    [Fact]
    public void ATreeThatReadsCleanlyReportsNothing()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "x", new UTF8Encoding(false));

        var (files, counters) = Walk(_root);

        Assert.Single(files);
        Assert.Equal(0, counters.DirectoriesUnreadable);
        Assert.Equal(string.Empty, MainForm.FormatCoverage(counters));
    }

    [Fact]
    public void ADeniedSubdirectoryIsCountedAndTheScanContinues()
    {
        string denied = Path.Combine(_root, "denied");
        Directory.CreateDirectory(denied);
        File.WriteAllText(Path.Combine(denied, "secret.txt"), "x", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(_root, "visible.txt"), "x", new UTF8Encoding(false));

        string user = $"{Environment.UserDomainName}\\{Environment.UserName}";

        using (Process? deny = Process.Start(new ProcessStartInfo(
            "icacls", $"\"{denied}\" /deny \"{user}:(OI)(CI)(RX)\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }))
        {
            deny?.WaitForExit(10000);

            if (deny is null || deny.ExitCode != 0)
                return; // icacls unavailable here; the ghost-root test still covers the counter.
        }

        try
        {
            var (files, counters) = Walk(_root);

            Assert.Equal("visible.txt", Path.GetFileName(Assert.Single(files)));
            Assert.Equal(1, counters.DirectoriesUnreadable);
        }
        finally
        {
            using Process? restore = Process.Start(new ProcessStartInfo(
                "icacls", $"\"{denied}\" /remove:d \"{user}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            restore?.WaitForExit(10000);
        }
    }
}
