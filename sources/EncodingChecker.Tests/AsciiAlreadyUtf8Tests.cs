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
/// UTF-16 original with a copy of the converted file and replaced the recovery record with one
/// describing that copy. <c>-FailOnChanges</c> also reported a change on every run.
/// <para>
/// The rule sits beside two others it must not displace: a source choice that contradicts
/// reliable detection is still refused, and a source-choice warning still outranks the rule's
/// own explanation. Each case runs a real surface - the CLI, a saved plan, the scan the GUI
/// and CLI share, or the GUI's orchestration - on real files.
/// </para>
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

    private const string AsciiExplanation = "ASCII is already valid UTF-8 without a BOM.";

    // A real conversion scan with backups on, as -From and -Target run it.
    private ConversionReportEntry ConvertOne(string? from, string target = "utf-8")
    {
        ScanEngine.ParseCharsetLabel(target, out string charset, out bool writeBom);
        var entries = new EntrySink();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                Action = ScanAction.Convert,
                SourceCharset = from,
                TargetCharset = charset,
                TargetWriteBom = writeBom,
                Backup = true,
                MaxParallelism = 1,
            },
            entries.Add,
            CancellationToken.None);

        return Assert.Single(entries);
    }

    // BOM-less UTF-16BE English text. Detection estimates utf-16BE, which the bytes cannot
    // prove, and every byte is also valid ASCII.
    private string WriteBomlessUtf16English()
    {
        string path = PathOf("bomless.txt");
        File.WriteAllBytes(
            path,
            new UnicodeEncoding(bigEndian: true, byteOrderMark: false)
                .GetBytes(string.Concat(Enumerable.Repeat("Hello, World! ", 40))));

        return path;
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
    public void ANonAsciiBytePastTheDetectionSampleIsAnErrorWithNoBackup()
    {
        // Detection sees only ASCII in its 64 KiB sample and names the file us-ascii. The
        // Unchanged path validates the whole file, and a UTF-8 "é" is not ASCII. The
        // validation error, not the ASCII explanation, is what the row reports.
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
                        StringComparison.Ordinal)
                    && !line.Contains(AsciiExplanation, StringComparison.Ordinal));

        Assert.Equal(before, File.ReadAllBytes(path));
        AssertNoBackupOrRecord(path);
    }

    [Fact]
    public void AnAsciiFileSaysWhyItNeedsNoConversion()
    {
        string path = WriteAscii();

        ConversionReportEntry entry = ConvertOne(from: null);

        Assert.Equal(ConversionRowResult.Unchanged, entry.Result);
        Assert.Null(entry.ReasonCode);
        Assert.Equal(AsciiExplanation, entry.Diagnostic);
        Assert.Contains(AsciiExplanation, ConversionReport.ToCsvString([entry]));
        AssertNoBackupOrRecord(path);
    }

    [Fact]
    public void AFileAlreadyInTheTargetCodecGetsNoExplanation()
    {
        // The explanation belongs to the ASCII rule; an ordinary match needs none.
        File.WriteAllBytes(PathOf("utf8.txt"), new UTF8Encoding(false).GetBytes("already 世界\n"));

        ConversionReportEntry entry = ConvertOne(from: null);

        Assert.Equal(ConversionRowResult.Unchanged, entry.Result);
        Assert.Null(entry.ReasonCode);
        Assert.Null(entry.Diagnostic);
    }

    [Fact]
    public void AnAsciiSourceChoiceThatContradictsReliableDetectionIsRefused()
    {
        // Strictly valid UTF-8 with non-ASCII text is reliable detection. Naming it ASCII is a
        // conflict, refused as such (exit 5), not left to fail validation as an error (exit 3).
        byte[] original = new UTF8Encoding(false).GetBytes("Hello 世界 and more text\n");
        string path = PathOf("utf8.txt");
        File.WriteAllBytes(path, original);

        (int exit, string output, _) = RunCaptured(
            "-BasePath", _root, "-From", "us-ascii", "-Target", "utf-8", "-Backup");

        Assert.Equal(ExpectedSafeRefusal, exit);
        Assert.Contains(
            output.Split(Environment.NewLine),
            line => line.StartsWith(path + ",", StringComparison.Ordinal)
                    && line.Contains(
                        $",Refused,{ConversionReasonCodes.ExplicitSourceConflictsWithDetection},",
                        StringComparison.Ordinal));

        Assert.Equal(original, File.ReadAllBytes(path));
        AssertNoBackupOrRecord(path);
    }

    [Fact]
    public void ThePolicyChecksAConflictingAsciiChoiceBeforeTheAsciiRule()
    {
        PlannedAction action = ConversionPolicy.Decide(
            "us-ascii", sourceCodePage: 20127, sourceHasBom: false,
            "utf-8", targetCodePage: 65001, targetHasBom: false,
            sourceWasSpecified: true, isUnicodeOrAscii: true,
            explicitSourceConflictsWithReliableDetection: true,
            automaticBomlessUnicodeDoubt: BomlessUnicodeKind.None,
            out SourceInterpretation interpretation, out _);

        Assert.Equal(PlannedAction.Refuse, action);
        Assert.Equal(SourceInterpretation.ExplicitSource, interpretation);
    }

    [Fact]
    public void AnAsciiChoiceOnBomlessUtf16KeepsItsWarningWhileLeftUnchanged()
    {
        // Every byte is valid ASCII, so the file is already UTF-8 under that choice. The choice
        // still contradicts EC's estimate, and the warning saying so outranks the explanation.
        string path = WriteBomlessUtf16English();
        byte[] original = File.ReadAllBytes(path);

        ConversionReportEntry entry = ConvertOne(from: "us-ascii");

        Assert.Equal(ConversionRowResult.Unchanged, entry.Result);
        Assert.Equal("utf-16BE", entry.DetectedEncodingLabel);
        Assert.Equal(
            ConversionReasonCodes.ExplicitSourceDiffersFromBomlessUnicodeEstimate,
            entry.ReasonCode);
        Assert.Contains("you selected us-ascii", entry.Diagnostic);
        Assert.Equal(original, File.ReadAllBytes(path));
        AssertNoBackupOrRecord(path);
    }

    [Fact]
    public void AChoiceMatchingAnUnprovableEstimateIsFlaggedWhenTheFileIsAlreadyInTheTarget()
    {
        // Not an ASCII case: the same code page leaves the file unchanged, and before, that
        // result carried no warning that the byte order was taken on trust.
        string authoritativeText = string.Concat(Enumerable.Repeat("\u4100\u0a00\u4200", 20));
        string path = PathOf("ambiguous.txt");
        File.WriteAllBytes(
            path, new UnicodeEncoding(bigEndian: true, byteOrderMark: false).GetBytes(authoritativeText));
        byte[] original = File.ReadAllBytes(path);

        ConversionReportEntry entry = ConvertOne(from: "utf-16", target: "utf-16");

        Assert.Equal(ConversionRowResult.Unchanged, entry.Result);
        Assert.Equal(
            ConversionReasonCodes.ExplicitSourceOnUnprovableBomlessUnicode,
            entry.ReasonCode);
        Assert.Contains("taken on trust", entry.Diagnostic);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void TheReviewShowsTheWarningForAFileTheChoiceLeavesUnchanged()
    {
        WriteBomlessUtf16English();
        ConversionReportEntry entry = ConvertOne(from: "us-ascii");

        ConversionPlan plan = ConversionPlan.FromEntries(
            [entry], _root, "utf-8", targetHasBom: false,
            backupEnabled: true, explicitSource: "us-ascii");

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);
            string text = string.Join("\n", Descendants(form).Select(c => c.Text));

            Assert.Contains("EC cannot prove the byte order", text);
            Assert.Contains("bomless.txt", text);
        });
    }

    private static IEnumerable<System.Windows.Forms.Control> Descendants(
        System.Windows.Forms.Control root)
    {
        foreach (System.Windows.Forms.Control child in root.Controls)
        {
            yield return child;

            foreach (System.Windows.Forms.Control nested in Descendants(child))
                yield return nested;
        }
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
