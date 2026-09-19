using System.Text;
using System.Windows.Forms;
using static EncodingChecker.Tests.CliRunner;
using static EncodingChecker.Tests.ExpectedExitCode;

namespace EncodingChecker.Tests;

/// <summary>
/// A file whose later bytes are not valid in its own codec is planned, summarized and shown to
/// the reviewer as a file that cannot be processed, not as one already in the target encoding.
/// </summary>
/// <remarks>
/// Detection reads at most 64 KiB, so a file can look like the target codec and still fail the
/// full-file check that follows. The scan reports that as an error; the plan built from the scan
/// must agree, because the plan summary and the confirmation dialog both read the planned action.
/// Each case scans a real directory and builds the plan from what the scan produced.
/// </remarks>
public sealed class InvalidFilePlanningTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_invalid_plan_").FullName;

    private string PlanPath => Path.Combine(_root, "plan.json");

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

    // Valid UTF-8 text well past the detection sample, spoiled at the end by an overlong "/",
    // which is invalid in UTF-8. Non-ASCII, so detection names UTF-8 and the file matches the
    // target codec, which is what makes the planner treat it as needing nothing.
    private static byte[] CorruptPastTheSample()
    {
        var text = new StringBuilder();

        while (Encoding.UTF8.GetByteCount(text.ToString()) < 70 * 1024)
            text.Append("中文内容测试 ");

        return [.. new UTF8Encoding(false).GetBytes(text.ToString()), 0xC0, 0xAF];
    }

    private string WriteCorrupt(string name = "corrupt.txt")
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, CorruptPastTheSample());

        return path;
    }

    // Already UTF-8 without a BOM, so nothing needs converting.
    private string WriteClean(string name = "clean.txt")
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes("already utf-8 世界\n"));

        return path;
    }

    // UTF-8 with a BOM, which converting to UTF-8 without one rewrites.
    private string WriteConvertible(string name = "convertible.txt")
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, "hello world", new UTF8Encoding(true));

        return path;
    }

    private (List<ConversionReportEntry> Entries, ConversionPlan Plan) ScanAndPlan()
    {
        var sink = new EntrySink();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                Action = ScanAction.Convert,
                TargetCharset = "utf-8",
                TargetWriteBom = false,
                WhatIf = true,
                MaxParallelism = 1,
            },
            sink.Add,
            CancellationToken.None);

        List<ConversionReportEntry> entries = [.. sink];

        ConversionPlan plan = ConversionPlan.FromEntries(
            entries, _root, "utf-8", targetHasBom: false,
            backupEnabled: false, explicitSource: null);

        return (entries, plan);
    }

    private static string AllText(Control root) =>
        string.Join("\n", Descendants(root).Select(c => c.Text));

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;

            foreach (Control nested in Descendants(child))
                yield return nested;
        }
    }

    [Fact]
    public void AnInvalidFileIsPlannedAsARefusalNotAsAlreadyInTheTargetEncoding()
    {
        WriteCorrupt();

        (List<ConversionReportEntry> entries, ConversionPlan plan) = ScanAndPlan();

        ConversionReportEntry entry = Assert.Single(entries);
        Assert.Equal(ConversionRowResult.Error, entry.Result);
        Assert.Equal(ConversionReasonCodes.StrictValidationFailed, entry.ReasonCode);

        PlannedFile planned = Assert.Single(plan.Files);
        Assert.Equal(PlannedAction.Refuse, planned.Action);
        Assert.Equal(ConversionReasonCodes.StrictValidationFailed, planned.ReasonCode);
        Assert.False(string.IsNullOrEmpty(planned.Reason));
        Assert.False(planned.NeedsSourceChoice);

        ConversionPlanSummary summary = plan.Summary;
        Assert.Equal(0, summary.AlreadyTarget);
        Assert.Equal(0, summary.NeedsSourceChoice);
        Assert.Equal(1, summary.OtherRefusals);
    }

    [Fact]
    public void ValidNeighboursKeepTheirOwnOutcomesAndTheTotalsAddUp()
    {
        WriteCorrupt();
        WriteClean();
        WriteConvertible();

        (_, ConversionPlan plan) = ScanAndPlan();
        ConversionPlanSummary summary = plan.Summary;

        Assert.Equal(3, summary.Selected);
        Assert.Equal(1, summary.ReadyToConvert);
        Assert.Equal(1, summary.AlreadyTarget);
        Assert.Equal(1, summary.OtherRefusals);
        Assert.Equal(0, summary.NeedsSourceChoice);
        Assert.Equal(0, summary.NotIdentified);

        // Every selected file has exactly one outcome.
        Assert.Equal(
            summary.Selected,
            summary.ReadyToConvert + summary.AlreadyTarget + summary.NotIdentified
            + summary.NeedsSourceChoice + summary.OtherRefusals);
    }

    [Fact]
    public void TheConfirmationDialogListsAnInvalidFileAsUnprocessableNotAsAlreadyCorrect()
    {
        WriteCorrupt();

        (_, ConversionPlan plan) = ScanAndPlan();

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);
            string text = AllText(form);

            // Categories with no files are hidden, so the corrupt file appearing under the
            // wrong one would show up as that label being present.
            Assert.Contains("Cannot be processed safely", text);
            Assert.DoesNotContain("Already in the target encoding", text);
        });
    }

    [Fact]
    public void ASavedPlanWithAnInvalidFileLeavesItByteIdenticalWhenApplied()
    {
        string corrupt = WriteCorrupt();
        string convertible = WriteConvertible();
        byte[] before = File.ReadAllBytes(corrupt);

        // Planning still writes the plan, and exits 3 because a file could not be processed.
        // A failed file outranks "changes needed" (2), which the valid convertible file would
        // otherwise report under -FailOnChanges.
        Assert.Equal(
            ExpectedProcessingErrors,
            Run(
                "-BasePath", _root, "-Target", "utf-8", "-Plan", PlanPath,
                "-FailOnChanges", "-Quiet"));

        // The saved plan says what the summary says.
        ConversionPlan? saved = ConversionPlan.Load(PlanPath, out string? error);
        Assert.Null(error);
        Assert.NotNull(saved);

        PlannedFile planned = Assert.Single(
            saved.Files, f => f.RelativePath == "corrupt.txt");
        Assert.Equal(PlannedAction.Refuse, planned.Action);
        Assert.Equal(ConversionReasonCodes.StrictValidationFailed, planned.ReasonCode);

        (int exit, _, string applyError) = RunCaptured("-Apply", PlanPath, "-Quiet");

        // Applying re-reads the whole file, so the invalid one is reported as a failure with its
        // reason and left as it was, while its valid neighbour is converted.
        Assert.Equal(ExpectedProcessingErrors, exit);
        Assert.Contains("corrupt.txt", applyError);
        Assert.Equal(before, File.ReadAllBytes(corrupt));
        Assert.False(TestContent.StillHasBom(convertible));
    }
}
