using System.Diagnostics;
using System.Text;
using static EncodingChecker.Tests.CliRunner;
using static EncodingChecker.Tests.ExpectedExitCode;

namespace EncodingChecker.Tests;

/// <summary>
/// When the command line cannot write an output it was asked to write, or a saved plan has gone
/// stale, it says so on stderr and exits 3. Files and folders it could not examine are counted on
/// stderr without changing the exit code.
/// </summary>
/// <remarks>
/// Exit code 3 is what a script checks after a failed write or a refused plan. The coverage notes
/// are a warning only: the run still exits 0 (docs/CLI.md). These cases provoke each condition for
/// real - a locked output file, more than twenty stale files, a backup file or a build folder in
/// the tree - so the message and the exit code are the ones a user would meet.
/// <para>
/// The output files are held open with <see cref="FileShare.None"/>, which makes replacing them
/// fail the way it does while another process has them open. A failed write must leave the
/// previous file whole and no staging file beside it. Where the conversion itself has already
/// happened by the time an output fails (the report and the journal are written after it), the
/// tests also check the source files, because "exit 3" must not be read as "nothing was changed".
/// </para>
/// </remarks>
public sealed class CliOutputFailureTests : IDisposable
{
    private static readonly string[] OutputFailureMessages =
    [
        "Failed to write the journal:",
        "Failed to write report file:",
        "Failed to write the plan:",
    ];

    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_cli_fail_").FullName;

    private string PlanPath => Path.Combine(_root, "plan.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory, or a folder whose access could not be restored, must
            // not fail the test run.
        }
    }

    // A UTF-8 file with a BOM, so converting to UTF-8 rewrites it and the BOM is the evidence.
    private string WriteSource(string relativePath)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "hello world", new UTF8Encoding(true));

        return path;
    }

    // An output file that already exists and is held open, so replacing it fails.
    private (string Path, FileStream Handle) HeldOutput(string name)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, "{}");

        return (path, new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None));
    }

    // Outputs are staged in a temporary file beside the destination; a failed write removes it.
    private void AssertNoStagingFileLeft()
    {
        Assert.Empty(
            Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(EncodingConverter.TempFileSuffix, StringComparison.Ordinal)));
    }

    private void MakePlan()
    {
        Assert.Equal(
            ExpectedClean,
            Run("-BasePath", _root, "-Target", "utf-8", "-Plan", PlanPath, "-Quiet"));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(25)]
    public void StaleFilesAreListedTwentyAtATimeAndTheRestCounted(int files)
    {
        const int ListedAtMost = 20;

        string[] sources =
        [
            .. Enumerable.Range(1, files).Select(i => WriteSource($"file{i:00}.txt")),
        ];

        MakePlan();

        // Every file changes after the plan was reviewed, so every file is stale.
        foreach (string path in sources)
        {
            File.WriteAllText(path, "changed since the plan", new UTF8Encoding(true));
        }

        (int exit, _, string error) = RunCaptured("-Apply", PlanPath, "-Quiet");

        string[] listed = error
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.EndsWith(
                "(contents changed since the plan was made)", StringComparison.Ordinal))
            .ToArray();

        int expectedListed = Math.Min(files, ListedAtMost);

        Assert.Equal(ExpectedProcessingErrors, exit);
        Assert.Equal(expectedListed, listed.Length);

        // Different files, not one file named repeatedly.
        Assert.Equal(expectedListed, listed.Distinct().Count());

        if (files > ListedAtMost)
        {
            Assert.Contains($"  ...and {files - ListedAtMost} more.", error);
        }
        else
        {
            Assert.DoesNotContain("...and", error);
        }

        // Nothing was converted: a plan that has gone stale is refused whole.
        Assert.All(sources, path => Assert.True(TestContent.StillHasBom(path), path));
    }

    [Fact]
    public void AnAppliedPlanWhoseJournalCannotBeWritten_HasConvertedButExitsThree()
    {
        string source = WriteSource("a.txt");
        MakePlan();

        (string journal, FileStream held) = HeldOutput("journal.json");

        using (held)
        {
            (int exit, _, string error) = RunCaptured(
                "-Apply", PlanPath, "-Quiet", "-Journal", journal);

            Assert.Equal(ExpectedProcessingErrors, exit);
            Assert.Contains("The conversion ran, but the journal could not be written:", error);
        }

        // The conversion is done; the message says so, and the exit code is not a claim otherwise.
        Assert.False(TestContent.StillHasBom(source));
        Assert.Equal("{}", File.ReadAllText(journal));
        AssertNoStagingFileLeft();
    }

    [Theory]
    [InlineData("journal", "-Journal", "Failed to write the journal:", true)]
    [InlineData("report", "-Report", "Failed to write report file:", true)]
    [InlineData("plan", "-Plan", "Failed to write the plan:", false)]
    public void AnOutputThatCannotBeWritten_ExitsThreeAndSaysWhich(
        string output, string option, string message, bool sourceIsConverted)
    {
        string source = WriteSource("a.txt");
        (string path, FileStream held) = HeldOutput($"held-{output}.out");

        using (held)
        {
            (int exit, _, string error) = RunCaptured(
                "-BasePath", _root, "-Target", "utf-8", option, path, "-Quiet");

            Assert.Equal(ExpectedProcessingErrors, exit);
            Assert.Contains(message, error);

            // Only the output that failed is blamed.
            Assert.All(
                OutputFailureMessages.Where(other => other != message),
                other => Assert.DoesNotContain(other, error));
        }

        // The failed write left the previous file whole and no staging file beside it.
        Assert.Equal("{}", File.ReadAllText(path));
        AssertNoStagingFileLeft();

        // The journal and the report are written after the conversion, so it has happened by then.
        // Planning never writes a source, so there is nothing to have converted.
        Assert.Equal(sourceIsConverted, !TestContent.StillHasBom(source));
    }

    [Fact]
    public void AJournalThatCannotBeWritten_FailsEvenForAPreview()
    {
        string source = WriteSource("a.txt");
        (string journal, FileStream held) = HeldOutput("journal.json");

        using (held)
        {
            (int exit, _, string error) = RunCaptured(
                "-BasePath", _root, "-Target", "utf-8", "-WhatIf", "-Journal", journal, "-Quiet");

            Assert.Equal(ExpectedProcessingErrors, exit);
            Assert.Contains("Failed to write the journal:", error);
        }

        Assert.Equal("{}", File.ReadAllText(journal));
        AssertNoStagingFileLeft();
        Assert.True(TestContent.StillHasBom(source));
    }

    // Outputs are written journal, then report, then plan, and the first failure ends the run. A
    // later output that was still written would leave a newer file next to an earlier one that
    // failed, which reads as a complete set.
    [Theory]
    [InlineData("-Journal", "-Report")]
    [InlineData("-Journal", "-Plan")]
    [InlineData("-Report", "-Plan")]
    public void AFailedOutputStopsTheOutputsThatWouldFollowIt(string failing, string following)
    {
        WriteSource("a.txt");
        (string heldPath, FileStream held) = HeldOutput("held.out");
        string followingPath = Path.Combine(_root, "following.out");

        using (held)
        {
            (int exit, _, _) = RunCaptured(
                "-BasePath", _root, "-Target", "utf-8",
                failing, heldPath, following, followingPath, "-Quiet");

            Assert.False(File.Exists(followingPath), $"{following} was written after {failing} failed");
            Assert.Equal(ExpectedProcessingErrors, exit);
        }

        AssertNoStagingFileLeft();
    }

    [Fact]
    public void AFailedPlanWrite_PrintsNoSummaryOfThePlan()
    {
        WriteSource("a.txt");

        // Control: a plan that is written is summarized when the run is not quiet, so the
        // absence below is not simply a summary that never prints.
        (int clean, string summary, _) = RunCaptured(
            "-BasePath", _root, "-Target", "utf-8", "-Plan", PlanPath);

        Assert.Equal(ExpectedClean, clean);
        Assert.Contains("Will convert:", summary);

        (string held, FileStream handle) = HeldOutput("held-plan.out");

        using (handle)
        {
            (int exit, string output, string error) = RunCaptured(
                "-BasePath", _root, "-Target", "utf-8", "-Plan", held);

            Assert.Equal(ExpectedProcessingErrors, exit);
            Assert.Contains("Failed to write the plan:", error);
            Assert.DoesNotContain("Will convert:", output);
        }
    }

    [Fact]
    public void AnOutputPathContainingNul_IsRejectedAsAUsageErrorNotThrown()
    {
        // An embedded NUL cannot arrive through a real Windows command line, so passing the
        // arguments in-process is the only way to reach the check. Argument parsing rejects it
        // first, with a usage error; the same check in the run-time output preflight can
        // therefore never be reached.
        WriteSource("a.txt");

        (int exit, _, string error) = RunCaptured(
            "-BasePath", _root, "-Target", "utf-8", "-WhatIf", "-Report", "bad\0name.csv", "-Quiet");

        Assert.Equal(ExpectedUsageError, exit);
        Assert.Contains("-Report contains an invalid path:", error);
    }

    [Fact]
    public void ABackupFileInTheTreeIsCountedAsNotExamined()
    {
        WriteSource("a.txt");
        WriteSource("a.txt.bak");

        (int exit, _, string error) = RunCaptured(
            "-BasePath", _root, "-Target", "utf-8", "-WhatIf", "-Quiet");

        Assert.Equal(ExpectedClean, exit);
        Assert.Contains(
            "1 matching file(s) not examined (EC backup, recovery record, or temporary file).",
            error);
    }

    [Fact]
    public void AFolderSkippedByNameIsCountedAsNotEntered()
    {
        WriteSource("a.txt");
        WriteSource(Path.Combine("build", "output.txt"));

        (int exit, _, string error) = RunCaptured(
            "-BasePath", _root, "-Target", "utf-8", "-WhatIf", "-Quiet");

        Assert.Equal(ExpectedClean, exit);
        Assert.Contains(
            "1 folder(s) not entered (build or metadata name); their contents were not counted.",
            error);
    }

    [Fact]
    public void AnUnreadableFolderIsCountedAsCouldNotBeRead()
    {
        WriteSource("a.txt");
        WriteSource(Path.Combine("denied", "secret.txt"));

        WithAccessDenied(Path.Combine(_root, "denied"), () =>
        {
            (int exit, _, string error) = RunCaptured(
                "-BasePath", _root, "-Target", "utf-8", "-WhatIf", "-Quiet");

            Assert.Equal(ExpectedClean, exit);
            Assert.Contains(
                "1 folder(s) could not be read; their contents were not examined.",
                error);
        });
    }

    [Fact]
    public void EachKindOfUnexaminedItemInOneTreeIsCountedOnItsOwnLine()
    {
        WriteSource("a.txt");
        WriteSource("a.txt.bak");
        WriteSource(Path.Combine("build", "output.txt"));
        WriteSource(Path.Combine("denied", "secret.txt"));

        WithAccessDenied(Path.Combine(_root, "denied"), () =>
        {
            (int exit, _, string error) = RunCaptured(
                "-BasePath", _root, "-Target", "utf-8", "-WhatIf", "-Quiet");

            Assert.Equal(ExpectedClean, exit);
            Assert.Contains(
                "1 matching file(s) not examined (EC backup, recovery record, or temporary file).",
                error);
            Assert.Contains(
                "1 folder(s) not entered (build or metadata name); their contents were not counted.",
                error);
            Assert.Contains(
                "1 folder(s) could not be read; their contents were not examined.",
                error);
        });
    }

    // Runs the body while the current user is denied read access to the folder.
    private static void WithAccessDenied(string folder, Action body)
    {
        string user = $"{Environment.UserDomainName}\\{Environment.UserName}";

        // A failure here must be loud: skipping would leave a test that passes having checked
        // nothing.
        Assert.True(
            Icacls($"\"{folder}\" /deny \"{user}:(OI)(CI)(RX)\""),
            "icacls could not deny access, so the unreadable-folder case was not exercised");

        bool restored;

        try
        {
            body();
        }
        finally
        {
            restored = Icacls($"\"{folder}\" /remove:d \"{user}\"");
        }

        Assert.True(restored, $"icacls could not restore access to {folder}");
    }

    private static bool Icacls(string arguments)
    {
        using Process? process = Process.Start(new ProcessStartInfo("icacls", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });

        if (process is null)
        {
            return false;
        }

        // Drain both streams while waiting, so a full pipe cannot stall the process.
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);

            return false;
        }

        return process.ExitCode == 0;
    }
}
