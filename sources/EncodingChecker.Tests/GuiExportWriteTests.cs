using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// The GUI's text and CSV exports never destroy an existing report by failing to write its
/// replacement.
/// </summary>
/// <remarks>
/// Both export commands write through <see cref="MainForm.WriteExportFile"/>, which stages the
/// new report in a temporary file and installs it only when it is complete. A failed write must
/// leave the previous report byte-for-byte as it was and no staging file beside it. The cases run
/// the same encoding and content each command uses; the save dialog around it is not exercised.
/// </remarks>
public sealed class GuiExportWriteTests : IDisposable
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_export_").FullName;

    private string ReportPath => Path.Combine(_root, "report.out");

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

    private static Encoding EncodingFor(bool csv) =>
        csv ? ConversionReport.CsvFileEncoding : new UTF8Encoding(true);

    private static ConversionReportEntry Row() => new()
    {
        FilePath = @"C:\scan\a.txt",
        SourceEncoding = "utf-8",
        TargetEncoding = "utf-8",
    };

    // The content each command writes: one line per file for the text export, the CSV report
    // for the other.
    private static Action<StreamWriter> Content(bool csv) => writer =>
    {
        if (csv)
            ConversionReport.WriteCsv([Row()], writer);
        else
            writer.WriteLine("utf-8\tC:\\scan\\a.txt");
    };

    private void AssertNoStagingFileLeft()
    {
        Assert.DoesNotContain(
            Directory.EnumerateFiles(_root),
            f => f.EndsWith(EncodingConverter.TempFileSuffix, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANewReportIsWrittenWithItsByteOrderMark(bool csv)
    {
        string? error = MainForm.WriteExportFile(ReportPath, EncodingFor(csv), Content(csv));

        Assert.Null(error);

        byte[] written = File.ReadAllBytes(ReportPath);
        Assert.Equal(Bom, written[..3]);
        Assert.Contains("a.txt", Encoding.UTF8.GetString(written, 3, written.Length - 3));
        AssertNoStagingFileLeft();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnExistingReportIsReplacedByTheCompleteNewOne(bool csv)
    {
        File.WriteAllText(ReportPath, "the previous report");

        string? error = MainForm.WriteExportFile(ReportPath, EncodingFor(csv), Content(csv));

        Assert.Null(error);

        byte[] written = File.ReadAllBytes(ReportPath);
        Assert.Equal(Bom, written[..3]);
        Assert.DoesNotContain("previous", Encoding.UTF8.GetString(written));
        AssertNoStagingFileLeft();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWriteThatFailsPartWayLeavesThePreviousReportUntouched(bool csv)
    {
        byte[] previous = Encoding.UTF8.GetBytes("the previous report\r\n");
        File.WriteAllBytes(ReportPath, previous);

        string? error = MainForm.WriteExportFile(
            ReportPath,
            EncodingFor(csv),
            writer =>
            {
                // A real prefix reaches the disk before the failure.
                Content(csv)(writer);
                writer.Flush();

                throw new IOException("the disk is full");
            });

        Assert.Equal("the disk is full", error);
        Assert.Equal(previous, File.ReadAllBytes(ReportPath));
        AssertNoStagingFileLeft();
    }

    [Fact]
    public void AFailedWriteOfANewReportLeavesNothingBehind()
    {
        string? error = MainForm.WriteExportFile(
            ReportPath,
            EncodingFor(csv: true),
            writer =>
            {
                Content(csv: true)(writer);
                writer.Flush();

                throw new IOException("the disk is full");
            });

        Assert.Equal("the disk is full", error);
        Assert.Empty(Directory.EnumerateFiles(_root));
    }

    [Fact]
    public void AReportHeldOpenByAnotherProgramIsNotReplacedAndTheReasonIsReturned()
    {
        byte[] previous = Encoding.UTF8.GetBytes("the previous report\r\n");
        File.WriteAllBytes(ReportPath, previous);

        string? error;

        using (new FileStream(ReportPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            error = MainForm.WriteExportFile(
                ReportPath, EncodingFor(csv: false), Content(csv: false));
        }

        Assert.False(string.IsNullOrEmpty(error));
        Assert.Equal(previous, File.ReadAllBytes(ReportPath));
        AssertNoStagingFileLeft();
    }
}
