using System.Text;
using static EncodingChecker.Tests.CliRunner;
using static EncodingChecker.Tests.ExpectedExitCode;

namespace EncodingChecker.Tests;

/// <summary>
/// A CSV report says <c>WouldConvert</c> for a file a preview only decided to convert, and
/// <c>Converted</c> only for a file that was written.
/// </summary>
/// <remarks>
/// A preview's CSV used to say <c>Converted</c> for files it never touched, and only the
/// journal marked the run as a preview. Each case reads the CSV a real surface writes.
/// </remarks>
public sealed class PreviewCsvResultTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_preview_csv_").FullName;

    private readonly string _outputs =
        Directory.CreateTempSubdirectory("ec_preview_csv_out_").FullName;

    public void Dispose()
    {
        foreach (string directory in new[] { _root, _outputs })
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    // UTF-16 with a BOM, which converting to UTF-8 rewrites.
    private string WriteConvertible()
    {
        string path = Path.Combine(_root, "a.txt");
        File.WriteAllText(path, "Hello 世界\r\n", Encoding.Unicode);

        return path;
    }

    // The Result cell of the row for this file.
    private static string ResultFor(string csv, string path)
    {
        string row = Assert.Single(
            csv.Split(Environment.NewLine),
            line => line.StartsWith(path + ",", StringComparison.Ordinal));

        return row.Split(',')[5];
    }

    private string Report => Path.Combine(_outputs, "report.csv");

    [Fact]
    public void APreviewReportSaysWouldConvertAndLeavesTheFileAlone()
    {
        string path = WriteConvertible();
        byte[] before = File.ReadAllBytes(path);

        (int exit, string output, _) = RunCaptured(
            "-BasePath", _root, "-Target", "utf-8", "-WhatIf", "-Report", Report);

        Assert.Equal(ExpectedClean, exit);
        Assert.Equal("WouldConvert", ResultFor(File.ReadAllText(Report), path));
        Assert.Equal("WouldConvert", ResultFor(output, path));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void PlanningSaysWouldConvert()
    {
        string path = WriteConvertible();

        (int exit, string output, _) = RunCaptured(
            "-BasePath", _root, "-Target", "utf-8", "-Plan", Path.Combine(_outputs, "plan.json"));

        Assert.Equal(ExpectedClean, exit);
        Assert.Equal("WouldConvert", ResultFor(output, path));
    }

    [Fact]
    public void ARealRunStillSaysConverted()
    {
        string path = WriteConvertible();

        Assert.Equal(
            ExpectedClean,
            Run("-BasePath", _root, "-Target", "utf-8", "-Report", Report, "-Quiet"));

        Assert.Equal("Converted", ResultFor(File.ReadAllText(Report), path));
        Assert.False(TestContent.StillHasBom(path));
    }

    [Fact]
    public void AGuiPreviewSaysWouldConvertAndALaterConversionOfTheSameRowsSaysConverted()
    {
        string path = WriteConvertible();
        byte[] before = File.ReadAllBytes(path);

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

        OrchestrationResult Run(bool preview) =>
            new ConversionOrchestrator(_ => ConfirmationResponse.Proceed).Run(
                rows, _root, "utf-8", targetWriteBom: false,
                backup: false, preview: preview,
                ScanEngine.DefaultMaxParallelism,
                _ => { },
                CancellationToken.None);

        Assert.Equal(OrchestrationOutcome.Previewed, Run(preview: true).Outcome);
        Assert.Equal("WouldConvert", ResultFor(ConversionReport.ToCsvString(rows), path));
        Assert.Equal(before, File.ReadAllBytes(path));

        // The real run's own deciding pass marks the row again; its write pass must clear it.
        Assert.Equal(OrchestrationOutcome.Converted, Run(preview: false).Outcome);
        Assert.Equal("Converted", ResultFor(ConversionReport.ToCsvString(rows), path));
    }

    [Fact]
    public void AnUnreachedRowStillSaysNotAttempted()
    {
        var entry = new ConversionReportEntry
        {
            FilePath = @"C:\scan\a.txt",
            SourceEncoding = "utf-16",
            TargetEncoding = "utf-8",
            Result = ConversionRowResult.Converted,
            ConversionOnlyDecided = true,
            NotAttempted = true,
        };

        Assert.Equal("NotAttempted", ResultFor(ConversionReport.ToCsvString([entry]), entry.FilePath));
    }
}
