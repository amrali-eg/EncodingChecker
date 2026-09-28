using System.Text;
using static EncodingChecker.Tests.CliRunner;
using static EncodingChecker.Tests.ExpectedExitCode;

namespace EncodingChecker.Tests;

/// <summary>
/// ASCII text is already UTF-8 without a BOM, so converting it to that target changes nothing
/// and must not replace the file's backup.
/// </summary>
/// <remarks>
/// A UTF-16 file of English text converts to ASCII bytes. Running the same command again used
/// to "convert" those bytes to themselves, and with backups on that replaced the backup of the
/// UTF-16 original with a copy of the converted file and removed the recovery record that
/// described it. <c>-FailOnChanges</c> also reported a change on every run. Each case runs the
/// real surface - the CLI, a saved plan, or the GUI's orchestration - on real files.
/// </remarks>
public sealed class AsciiAlreadyUtf8Tests : IDisposable
{
    private const string EnglishText = "plain English text\r\nsecond line\r\n";

    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_ascii_utf8_").FullName;

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

    private string PathOf(string name) => Path.Combine(_root, name);

    // UTF-16 with a BOM, holding text that is ASCII once converted.
    private string WriteUtf16English(string name = "english.txt")
    {
        string path = PathOf(name);
        File.WriteAllText(path, EnglishText, Encoding.Unicode);

        return path;
    }

    private string WriteAscii(string name = "ascii.txt")
    {
        string path = PathOf(name);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(EnglishText));

        return path;
    }

    // Everything a repeat run could disturb: the file, its backup, and its recovery record.
    private static byte[][] Evidence(string path) =>
    [
        File.ReadAllBytes(path),
        File.ReadAllBytes(path + ".bak"),
        File.ReadAllBytes(ConversionMetadataStore.MetadataPathFor(path)),
    ];

    private static void AssertNoBackupOrRecord(string path)
    {
        Assert.False(File.Exists(path + ".bak"));
        Assert.False(File.Exists(ConversionMetadataStore.MetadataPathFor(path)));
    }

    [Fact]
    public void RepeatingABackedUpConversionKeepsTheOriginalBackup()
    {
        string path = WriteUtf16English();
        byte[] original = File.ReadAllBytes(path);

        Assert.Equal(ExpectedClean, Run("-BasePath", _root, "-Target", "utf-8", "-Backup", "-Quiet"));

        // The first run converted the file and backed up the UTF-16 original.
        Assert.Equal(Encoding.ASCII.GetBytes(EnglishText), File.ReadAllBytes(path));
        Assert.Equal(original, File.ReadAllBytes(path + ".bak"));
        byte[][] afterFirstRun = Evidence(path);

        (int exit, string output, _) =
            RunCaptured("-BasePath", _root, "-Target", "utf-8", "-Backup");

        Assert.Equal(ExpectedClean, exit);
        Assert.Contains(
            output.Split(Environment.NewLine),
            line => line.StartsWith(path + ",us-ascii,", StringComparison.Ordinal)
                    && line.Contains(",Unchanged,", StringComparison.Ordinal));

        // The file, the backup of the original, and the record describing it are untouched.
        Assert.Equal(afterFirstRun, Evidence(path));
    }

    [Fact]
    public void FailOnChangesPassesOnceTheFileIsAlreadyUtf8()
    {
        WriteUtf16English();

        Assert.Equal(
            ExpectedChangesNeeded,
            Run("-BasePath", _root, "-Target", "utf-8", "-WhatIf", "-FailOnChanges", "-Quiet"));

        Assert.Equal(ExpectedClean, Run("-BasePath", _root, "-Target", "utf-8", "-Quiet"));

        Assert.Equal(
            ExpectedClean,
            Run("-BasePath", _root, "-Target", "utf-8", "-WhatIf", "-FailOnChanges", "-Quiet"));
    }

    [Fact]
    public void ANonAsciiBytePastTheDetectionSampleIsStillRefused()
    {
        // Detection sees only ASCII in its 64 KiB sample and names the file us-ascii. The
        // unchanged decision reads the rest, where a UTF-8 "é" is not ASCII.
        var text = new StringBuilder();

        while (text.Length < 70 * 1024)
            text.Append("the quick brown fox jumps over the lazy dog. ");

        text.Append("café\n");

        string path = PathOf("late.txt");
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text.ToString()));
        byte[] before = File.ReadAllBytes(path);

        (int exit, string output, _) =
            RunCaptured("-BasePath", _root, "-Target", "utf-8", "-Backup");

        Assert.Equal(ExpectedProcessingErrors, exit);
        Assert.Contains(
            output.Split(Environment.NewLine),
            line => line.StartsWith(path + ",us-ascii,", StringComparison.Ordinal)
                    && line.Contains(
                        $",Error,{ConversionReasonCodes.StrictValidationFailed},",
                        StringComparison.Ordinal));

        Assert.Equal(before, File.ReadAllBytes(path));
        AssertNoBackupOrRecord(path);
    }

    [Fact]
    public void ASavedPlanRecordsAsciiAsAlreadyInTheTargetAndApplyingItLeavesItAlone()
    {
        string ascii = WriteAscii();
        string english = WriteUtf16English();
        byte[] asciiBefore = File.ReadAllBytes(ascii);
        string planPath = Path.Combine(
            Directory.CreateTempSubdirectory("ec_ascii_plan_").FullName, "plan.json");

        try
        {
            Assert.Equal(
                ExpectedClean,
                Run("-BasePath", _root, "-Target", "utf-8", "-Backup", "-Plan", planPath, "-Quiet"));

            ConversionPlan? plan = ConversionPlan.Load(planPath, out string? error);
            Assert.Null(error);
            Assert.NotNull(plan);
            Assert.Equal(
                PlannedAction.Unchanged,
                Assert.Single(plan.Files, f => f.RelativePath == "ascii.txt").Action);
            Assert.Equal(
                PlannedAction.Convert,
                Assert.Single(plan.Files, f => f.RelativePath == "english.txt").Action);
            Assert.Equal(1, plan.Summary.AlreadyTarget);

            Assert.Equal(ExpectedClean, Run("-Apply", planPath, "-Quiet"));

            Assert.Equal(asciiBefore, File.ReadAllBytes(ascii));
            AssertNoBackupOrRecord(ascii);
            Assert.Equal(Encoding.ASCII.GetBytes(EnglishText), File.ReadAllBytes(english));
            Assert.True(File.Exists(english + ".bak"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(planPath)!, recursive: true);
        }
    }

    [Fact]
    public void TheGuiLeavesAsciiAloneAndARepeatedConvertKeepsTheOriginalBackup()
    {
        string ascii = WriteAscii();
        string english = WriteUtf16English();
        byte[] asciiBefore = File.ReadAllBytes(ascii);

        // The rows the View button produces; the GUI converts the same rows again later.
        var scanned = new EntrySink();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                IncludeSubdirectories = true,
                IncludePatterns = ["*"],
                Action = ScanAction.Detect,
            },
            scanned.Add,
            CancellationToken.None);

        List<ConversionReportEntry> rows = [.. scanned];
        Assert.Equal(2, rows.Count);

        OrchestrationResult Convert() =>
            new ConversionOrchestrator(_ => ConfirmationResponse.Proceed).Run(
                rows, _root, "utf-8", targetWriteBom: false,
                backup: true, preview: false,
                ScanEngine.DefaultMaxParallelism,
                _ => { },
                CancellationToken.None);

        OrchestrationResult first = Convert();

        Assert.Equal(OrchestrationOutcome.Converted, first.Outcome);
        Assert.Equal(
            PlannedAction.Unchanged,
            Assert.Single(first.Plan!.Files, f => f.RelativePath == "ascii.txt").Action);
        Assert.Equal(asciiBefore, File.ReadAllBytes(ascii));
        AssertNoBackupOrRecord(ascii);

        byte[][] afterFirstRun = Evidence(english);

        Convert();

        Assert.All(rows, row => Assert.Equal(ConversionRowResult.Unchanged, row.Result));
        Assert.Equal(afterFirstRun, Evidence(english));
        Assert.Equal(asciiBefore, File.ReadAllBytes(ascii));
        AssertNoBackupOrRecord(ascii);
    }
}
