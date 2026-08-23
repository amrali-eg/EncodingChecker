using System.Text;
using System.Windows.Forms;

namespace EncodingChecker.Tests;

/// <summary>
/// One file, one row, one authoritative ConversionReportEntry.
///
/// A row's entry lives in ListViewItem.Tag and is what OnExportReport writes to CSV,
/// while the GUI's per-row presentation is driven by whatever entry came back from
/// processing. Those are normally the same instance, because processing mutates the
/// entry in place - but ScanEngine.RunParallel substitutes a fresh error entry when a
/// file throws, and a substitute that never reached Tag would leave the exported CSV
/// reporting a stale pre-error result for a row the GUI showed as failed.
/// </summary>
public sealed class ResultEntryReconciliationTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_reconcile_").FullName;

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

    private static ListViewItem Row(ConversionReportEntry entry) =>
        new([
            ScanEngine.FormatCharsetLabel(entry.SourceEncoding, entry.SourceHasBom),
            Path.GetFileName(entry.FilePath),
            Path.GetExtension(entry.FilePath),
            Path.GetDirectoryName(entry.FilePath) ?? string.Empty,
        ])
        {
            Tag = entry,
        };

    private static ConversionReportEntry ScannedEntry(string path) => new()
    {
        FilePath = path,
        SourceEncoding = "windows-1252",
        SourceHasBom = false,
        TargetEncoding = "windows-1252",
        TargetHasBom = false,
        Result = ConversionRowResult.Unchanged,
    };

    private static string[] CsvValuesFor(ListViewItem item)
    {
        var entry = (ConversionReportEntry)item.Tag!;
        string csv = ConversionReport.ToCsvString([entry]);

        return csv
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)[1]
            .Split(',');
    }

    [Fact]
    public void SubstituteErrorEntry_ReachesTheRow_SoCsvExportReportsTheError()
    {
        // The bug: RunParallel builds a *different* entry object for a file that threw,
        // the row kept the original, and the CSV therefore said "Unchanged" for a file
        // the GUI had just marked failed.
        ConversionReportEntry scanned = ScannedEntry(Path.Combine(_root, "boom.txt"));
        ListViewItem item = Row(scanned);
        item.Checked = true;

        var substitute = new ConversionReportEntry
        {
            FilePath = scanned.FilePath,
            SourceEncoding = "(Error)",
            TargetEncoding = "(Error)",
            Result = ConversionRowResult.Error,
            Diagnostic = "Access to the path is denied.",
        };

        MainForm.UpdateResultItem(item, substitute, targetLabel: "utf-8", wasPreview: false);

        // The row now owns the outcome that was actually reported...
        var authoritative = (ConversionReportEntry)item.Tag!;
        Assert.Same(substitute, authoritative);
        Assert.Equal(ConversionRowResult.Error, authoritative.Result);
        Assert.Equal("Access to the path is denied.", authoritative.Diagnostic);

        // ...and the export agrees with it, instead of reporting the stale scan result.
        string[] values = CsvValuesFor(item);
        Assert.Equal("Error", values[5]);
        Assert.NotEqual("Unchanged", values[5]);

        // Presentation is unchanged by this fix: an error row keeps its charset and
        // stays checked so it can be retried.
        Assert.True(item.Checked);
    }

    [Fact]
    public void NonErrorRowsProcessedAlongsideAnError_ExportTheirOwnResults()
    {
        ConversionReportEntry okScan = ScannedEntry(Path.Combine(_root, "ok.txt"));
        ConversionReportEntry failScan = ScannedEntry(Path.Combine(_root, "fail.txt"));

        ListViewItem okItem = Row(okScan);
        ListViewItem failItem = Row(failScan);

        // The successful file's entry is mutated in place, as processing normally does.
        okScan.Result = ConversionRowResult.Converted;
        okScan.TargetEncoding = "utf-8";
        okScan.TargetHasBom = true;

        var substitute = new ConversionReportEntry
        {
            FilePath = failScan.FilePath,
            SourceEncoding = "(Error)",
            TargetEncoding = "(Error)",
            Result = ConversionRowResult.Error,
            Diagnostic = "The process cannot access the file.",
        };

        MainForm.UpdateResultItem(okItem, okScan, "utf-8-bom", wasPreview: false);
        MainForm.UpdateResultItem(failItem, substitute, "utf-8-bom", wasPreview: false);

        Assert.Equal("Converted", CsvValuesFor(okItem)[5]);
        Assert.Equal("Error", CsvValuesFor(failItem)[5]);
    }

    [Theory]
    [InlineData("Unchanged")]
    [InlineData("Skipped")]
    [InlineData("Converted")]
    public void InPlaceMutatedEntries_StillExportTheirFinalResult(string resultName)
    {
        // ConversionRowResult is internal, so a public [Theory] method can't take it as
        // a parameter (CS0051); round-trip through its name instead, as
        // ConversionReportCsvTests already does.
        var result = Enum.Parse<ConversionRowResult>(resultName);

        // The ordinary path: the same instance is scanned, processed and exported.
        ConversionReportEntry entry = ScannedEntry(Path.Combine(_root, "f.txt"));
        ListViewItem item = Row(entry);

        entry.Result = result;

        MainForm.UpdateResultItem(item, entry, "utf-8", wasPreview: false);

        Assert.Same(entry, item.Tag);
        Assert.Equal(resultName, CsvValuesFor(item)[5]);
    }

    [Fact]
    public void PreviewResult_DoesNotLeaveTheRowExportingAStaleEntry()
    {
        ConversionReportEntry entry = ScannedEntry(Path.Combine(_root, "preview.txt"));
        ListViewItem item = Row(entry);
        item.Checked = true;

        entry.Result = ConversionRowResult.Converted; // "would convert"

        MainForm.UpdateResultItem(item, entry, "utf-8", wasPreview: true);

        Assert.Same(entry, item.Tag);
        Assert.True(item.Checked);
    }

    [Fact]
    public void CsvExportedFromTheRow_KeepsTheSixColumnSchemaAndEscaping()
    {
        // Guards the export path this fix touches: schema and quoting are unaffected.
        var entry = new ConversionReportEntry
        {
            FilePath = Path.Combine(_root, "has, comma.txt"),
            SourceEncoding = "windows-1252",
            SourceHasBom = false,
            TargetEncoding = "utf-8",
            TargetHasBom = true,
            Result = ConversionRowResult.Error,
            Diagnostic = "should never appear in the CSV",
        };

        ListViewItem item = Row(entry);
        MainForm.UpdateResultItem(item, entry, "utf-8-bom", wasPreview: false);

        string csv = ConversionReport.ToCsvString([(ConversionReportEntry)item.Tag!]);
        string[] lines = csv.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("File,Encoding,BOM,Target,TargetBOM,Result", lines[0]);
        Assert.StartsWith("\"" + entry.FilePath + "\",", lines[1]);
        Assert.EndsWith(",Error", lines[1]);
        Assert.DoesNotContain("should never appear", csv);
    }

    [Fact]
    public void ConvertFiles_EmitsTheSameEntryInstanceItWasGiven_ForSuccessAndFailure()
    {
        // The engine half of the invariant: a caller that holds an entry (as the GUI
        // does, in ListViewItem.Tag) sees its own object updated, rather than an
        // outcome recorded on some other instance it never learns about.
        string converts = Path.Combine(_root, "converts.txt");
        File.WriteAllText(converts, "hello", new UTF8Encoding(false));

        string fails = Path.Combine(_root, "missing.txt"); // never created

        ConversionReportEntry convertsEntry = new()
        {
            FilePath = converts,
            SourceEncoding = "utf-8",
            SourceHasBom = false,
            TargetEncoding = "utf-8",
        };

        ConversionReportEntry failsEntry = new()
        {
            FilePath = fails,
            SourceEncoding = "utf-8",
            SourceHasBom = false,
            TargetEncoding = "utf-8",
        };

        var completed = new List<ConversionReportEntry>();

        ScanEngine.ConvertFiles(
            [convertsEntry, failsEntry],
            "utf-16",
            targetWriteBom: true,
            maxParallelism: 1,
            whatIf: false,
            backup: false,
            completed.Add,
            CancellationToken.None);

        Assert.Equal(2, completed.Count);
        Assert.Same(convertsEntry, completed.Single(e => e.FilePath == converts));
        Assert.Same(failsEntry, completed.Single(e => e.FilePath == fails));

        Assert.Equal(ConversionRowResult.Converted, convertsEntry.Result);
        Assert.Equal(ConversionRowResult.Error, failsEntry.Result);
        Assert.NotNull(failsEntry.Diagnostic);
    }
}
