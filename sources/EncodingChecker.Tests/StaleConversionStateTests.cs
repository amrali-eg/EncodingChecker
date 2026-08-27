using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// A ConversionReportEntry keeps its original-scan SourceEncoding/SourceHasBom for
/// reporting, so after this tool converts a file those values no longer describe what
/// is on disk. CurrentCharsetLabel carries the real state forward; without it a second
/// conversion of the same row decodes the new file with the old encoding, which can
/// silently produce mojibake that neither strict decoding nor the SHA-256 verification
/// catches - both sides of the comparison would use the same wrong encoding.
/// </summary>
public sealed class StaleConversionStateTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_stale_state_").FullName;

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

    private static ConversionReportEntry Entry(
        string path,
        string sourceEncoding,
        bool sourceHasBom = false) =>
        new()
        {
            FilePath = path,
            SourceEncoding = sourceEncoding,
            SourceHasBom = sourceHasBom,
            TargetEncoding = sourceEncoding,
            TargetHasBom = sourceHasBom,

            // These name the source encoding rather than having it detected, which is
            // what -From means. Without saying so, the ambiguity gate correctly refuses
            // single-byte content whose encoding its bytes do not identify.
            SourceEncodingWasSpecified = true,
        };

    private static ConversionReportEntry Convert(
        ConversionReportEntry entry,
        string targetCharset,
        bool targetWriteBom,
        bool whatIf = false,
        bool backup = false)
    {
        var completed = new List<ConversionReportEntry>();

        ScanEngine.ConvertFiles(
            [entry],
            targetCharset,
            targetWriteBom,
            ScanEngine.DefaultMaxParallelism,
            whatIf: whatIf,
            backup: backup,
            completed.Add,
            CancellationToken.None);

        return Assert.Single(completed);
    }

    [Fact]
    public void Windows1252_ThenUtf8_ThenSecondConvert_DoesNotCorrupt()
    {
        // The exact GUI sequence: View, Convert, then Convert the same row again.
        // Decoding the now-UTF-8 bytes as windows-1252 does not throw - that code page
        // maps almost every byte value - so nothing downstream would catch the damage.
        const string original = "café naïve";

        string path = Path.Combine(_root, "cafe.txt");
        File.WriteAllBytes(path, Encoding.GetEncoding("windows-1252").GetBytes(original));

        ConversionReportEntry entry = Entry(path, "windows-1252");

        Convert(entry, "utf-8", targetWriteBom: false);
        Assert.Equal(ConversionRowResult.Converted, entry.Result);
        Assert.Equal(original, new UTF8Encoding(false).GetString(File.ReadAllBytes(path)));

        Convert(entry, "utf-16", targetWriteBom: true);

        string finalText = File.ReadAllText(path, Encoding.Unicode);
        Assert.Equal(original, finalText);
        Assert.DoesNotContain("Ã", finalText, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreeSuccessiveConvertsAcrossTargets_PreserveContent()
    {
        const string original = "café naïve — ünïcödé";

        string path = Path.Combine(_root, "chain.txt");
        File.WriteAllText(path, original, new UTF8Encoding(false));

        ConversionReportEntry entry = Entry(path, "utf-8");

        Convert(entry, "utf-16", targetWriteBom: true);
        Convert(entry, "utf-8", targetWriteBom: true);
        Convert(entry, "utf-16", targetWriteBom: false);

        Assert.Equal(ConversionRowResult.Converted, entry.Result);
        Assert.Equal(
            original,
            new UnicodeEncoding(bigEndian: false, byteOrderMark: false)
                .GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void SecondConvertToTheSameTarget_ReportsUnchanged()
    {
        // A pure-ASCII file converted to UTF-8 without a BOM keeps identical bytes, so
        // re-detecting would report "us-ascii" again and convert a second time. The
        // label recorded at install time says "utf-8", which is what the file now
        // declares, so the repeat is correctly recognised as a no-op.
        string path = Path.Combine(_root, "ascii.txt");
        File.WriteAllText(path, TestContent.Ascii, Encoding.ASCII);

        ConversionReportEntry entry = Entry(path, "us-ascii");

        Convert(entry, "utf-8", targetWriteBom: false);
        Assert.Equal(ConversionRowResult.Converted, entry.Result);

        Convert(entry, "utf-8", targetWriteBom: false);
        Assert.Equal(ConversionRowResult.Unchanged, entry.Result);
    }

    [Fact]
    public void SuccessfulConversion_SetsCurrentCharsetLabel()
    {
        string path = Path.Combine(_root, "ok.txt");
        File.WriteAllText(path, TestContent.Multilingual, new UTF8Encoding(false));

        ConversionReportEntry entry = Entry(path, "utf-8");
        Assert.Null(entry.CurrentCharsetLabel);

        Convert(entry, "utf-16", targetWriteBom: true);

        Assert.Equal(ConversionRowResult.Converted, entry.Result);
        Assert.Equal("utf-16-bom", entry.CurrentCharsetLabel);

        // The original scan values are what the CSV report describes, so they must not
        // be rewritten by a conversion.
        Assert.Equal("utf-8", entry.SourceEncoding);
        Assert.False(entry.SourceHasBom);
    }

    [Fact]
    public void Preview_DoesNotSetCurrentCharsetLabel()
    {
        string path = Path.Combine(_root, "preview.txt");
        File.WriteAllText(path, TestContent.Multilingual, new UTF8Encoding(false));

        ConversionReportEntry entry = Entry(path, "utf-8");

        Convert(entry, "utf-16", targetWriteBom: true, whatIf: true);

        Assert.Equal(ConversionRowResult.Converted, entry.Result); // "would convert"
        Assert.Null(entry.CurrentCharsetLabel);
    }

    [Fact]
    public void Unchanged_DoesNotSetCurrentCharsetLabel()
    {
        string path = Path.Combine(_root, "match.txt");
        File.WriteAllText(path, TestContent.Multilingual, new UTF8Encoding(true));

        ConversionReportEntry entry = Entry(path, "utf-8", sourceHasBom: true);

        Convert(entry, "utf-8", targetWriteBom: true);

        Assert.Equal(ConversionRowResult.Unchanged, entry.Result);
        Assert.Null(entry.CurrentCharsetLabel);
    }

    [Fact]
    public void FailedConversion_DoesNotSetCurrentCharsetLabel_AndStaysRetryable()
    {
        // Cyrillic cannot be represented in windows-1252, so the strict encoder throws
        // and nothing is installed - the row must remain describable by its scan data.
        string path = Path.Combine(_root, "fail.txt");
        File.WriteAllText(path, "Привет", new UTF8Encoding(false));
        byte[] originalBytes = File.ReadAllBytes(path);

        ConversionReportEntry entry = Entry(path, "utf-8");

        Convert(entry, "windows-1252", targetWriteBom: false);

        Assert.Equal(ConversionRowResult.Error, entry.Result);
        Assert.Null(entry.CurrentCharsetLabel);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));

        // Retrying against a target that can represent the content still works, and
        // clears the diagnostic left by the failed attempt.
        Convert(entry, "utf-16", targetWriteBom: true);

        Assert.Equal(ConversionRowResult.Converted, entry.Result);
        Assert.Null(entry.Diagnostic);
        Assert.Equal("utf-16-bom", entry.CurrentCharsetLabel);
    }

    [Fact]
    public void UnknownSourceEncoding_IsSkipped_NotDecodedWithAGuess()
    {
        string path = Path.Combine(_root, "unknown.txt");
        File.WriteAllText(path, TestContent.Ascii, Encoding.ASCII);

        ConversionReportEntry entry = Entry(path, ScanEngine.UNKNOWN_CHARSET);
        byte[] originalBytes = File.ReadAllBytes(path);

        Convert(entry, "utf-16", targetWriteBom: true);

        Assert.Equal(ConversionRowResult.Skipped, entry.Result);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void CsvReport_StillUsesOriginalSourceEncoding_AfterConversion()
    {
        string path = Path.Combine(_root, "report.txt");
        File.WriteAllBytes(
            path,
            Encoding.GetEncoding("windows-1252").GetBytes("café"));

        ConversionReportEntry entry = Entry(path, "windows-1252");

        Convert(entry, "utf-8", targetWriteBom: true);

        string csv = ConversionReport.ToCsvString([entry]);
        string[] lines = csv.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        string[] values = lines[1].Split(',');

        // The report describes the conversion that happened: from windows-1252 to
        // utf-8-with-BOM. CurrentCharsetLabel is internal state and never appears.
        Assert.Equal("windows-1252", values[1]);  // Encoding   (original)
        Assert.Equal("No", values[2]);            // BOM        (original)
        Assert.Equal("utf-8", values[3]);         // Target
        Assert.Equal("Yes", values[4]);           // TargetBOM
        Assert.Equal("Converted", values[5]);     // Result
        Assert.DoesNotContain("utf-8-bom", csv, StringComparison.Ordinal);
    }
}
