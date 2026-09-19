using System.Diagnostics;
using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// The GUI's text and CSV exports never destroy an existing report by failing to write its
/// replacement.
/// </summary>
/// <remarks>
/// Both export commands write through <see cref="MainForm.WriteExportFile"/>, which stages the
/// new report in a temporary file and installs it only when it is complete. A failed write must
/// leave the previous report byte-for-byte as it was and no staging file beside it. A read-only
/// report or a link is refused instead of being replaced. The cases use the encodings and content
/// writers the commands themselves use; the save dialog around the write is not exercised.
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
            // A report left read-only by a case would otherwise stop the folder being deleted.
            if (File.Exists(ReportPath))
                File.SetAttributes(ReportPath, FileAttributes.Normal);

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory must not fail the test run.
        }
    }

    private static Encoding EncodingFor(bool csv) =>
        csv ? ConversionReport.CsvFileEncoding : MainForm.TextExportEncoding;

    private static ConversionReportEntry Row() => new()
    {
        FilePath = @"C:\scan\a.txt",
        SourceEncoding = "utf-8",
        TargetEncoding = "utf-8",
    };

    // The content each command writes.
    private static Action<StreamWriter> Content(bool csv) => writer =>
    {
        if (csv)
            ConversionReport.WriteCsv([Row()], writer);
        else
            MainForm.WriteTextExport([("utf-8", @"C:\scan", "a.txt")], writer);
    };

    // What the file must hold: the byte-order mark, then exactly the text the writer produces.
    private static byte[] ExpectedBytes(bool csv)
    {
        using var text = new StringWriter();

        if (csv)
            ConversionReport.WriteCsv([Row()], text);
        else
            MainForm.WriteTextExport([("utf-8", @"C:\scan", "a.txt")], text);

        return [.. Bom, .. new UTF8Encoding(false).GetBytes(text.ToString())];
    }

    private void AssertNoStagingFileLeft()
    {
        Assert.DoesNotContain(
            Directory.EnumerateFiles(_root),
            f => f.EndsWith(EncodingConverter.TempFileSuffix, StringComparison.Ordinal));
    }

    [Fact]
    public void TheTextExportListsTheCharsetThenTheFullPath()
    {
        // The line format is what other tools read back, so it is pinned outside the helper.
        using var text = new StringWriter();
        MainForm.WriteTextExport([("utf-8", @"C:\scan", "a.txt")], text);

        Assert.Equal("utf-8\tC:\\scan\\a.txt" + Environment.NewLine, text.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANewReportIsWrittenWithItsByteOrderMark(bool csv)
    {
        string? error = MainForm.WriteExportFile(ReportPath, EncodingFor(csv), Content(csv));

        Assert.Null(error);
        Assert.Equal(ExpectedBytes(csv), File.ReadAllBytes(ReportPath));
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
        Assert.Equal(ExpectedBytes(csv), File.ReadAllBytes(ReportPath));
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

    [Fact]
    public void ADestinationInAFolderThatDoesNotExistIsAnErrorAndCreatesNothing()
    {
        string missing = Path.Combine(_root, "no-such-folder", "report.out");

        string? error = MainForm.WriteExportFile(
            missing, EncodingFor(csv: true), Content(csv: true));

        Assert.False(string.IsNullOrEmpty(error));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void AReadOnlyReportIsRefusedNotOverwritten()
    {
        // A direct write failed on a read-only file; the atomic install would have cleared the
        // flag and replaced it, so the refusal is what keeps that protection.
        byte[] previous = Encoding.UTF8.GetBytes("the previous report\r\n");
        File.WriteAllBytes(ReportPath, previous);
        File.SetAttributes(ReportPath, FileAttributes.ReadOnly);

        string? error = MainForm.WriteExportFile(
            ReportPath, EncodingFor(csv: true), Content(csv: true));

        Assert.NotNull(error);
        Assert.Contains("read-only", error);
        Assert.Equal(previous, File.ReadAllBytes(ReportPath));
        Assert.True(File.GetAttributes(ReportPath).HasFlag(FileAttributes.ReadOnly));
        AssertNoStagingFileLeft();
    }

    [Fact]
    public void ALinkAsTheDestinationIsRefusedNotReplaced()
    {
        // A junction is the link a test can create without special privileges; a file symlink
        // carries the same ReparsePoint attribute the refusal checks.
        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);

        string link = Path.Combine(_root, "link");
        Assert.True(
            RunCmd($"mklink /J \"{link}\" \"{target}\"") && Directory.Exists(link),
            "The junction fixture could not be created.");

        try
        {
            string? error = MainForm.WriteExportFile(
                link, EncodingFor(csv: true), Content(csv: true));

            Assert.NotNull(error);
            Assert.Contains("is a link", error);

            // The link is still a link and nothing was written through it.
            Assert.True(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint));
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        }
        finally
        {
            Assert.True(RunCmd($"rmdir \"{link}\""));
        }
    }

    private static bool RunCmd(string command)
    {
        using Process? process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c {command}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        if (process is null)
            return false;

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
