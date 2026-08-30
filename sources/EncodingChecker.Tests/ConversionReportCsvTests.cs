namespace EncodingChecker.Tests;

/// <summary>ConversionReport.WriteCsv/ToCsvString — header shape, field quoting, and escaping.</summary>
public sealed class ConversionReportCsvTests
{
    [Fact]
    public void CsvFileEncoding_IsExplicitUtf8WithBom()
    {
        Assert.Equal([0xEF, 0xBB, 0xBF], ConversionReport.CsvFileEncoding.GetPreamble());
    }

    private static ConversionReportEntry Entry(
        string filePath = "file.txt",
        string sourceEncoding = "utf-8",
        bool sourceHasBom = false,
        string targetEncoding = "utf-8",
        bool targetHasBom = false,
        ConversionRowResult result = ConversionRowResult.Unchanged) =>
        new()
        {
            FilePath = filePath,
            SourceEncoding = sourceEncoding,
            SourceHasBom = sourceHasBom,
            TargetEncoding = targetEncoding,
            TargetHasBom = targetHasBom,
            Result = result,
        };

    [Fact]
    public void WriteCsv_HeaderColumnNames_AreAllDistinct_SoStrictParsersCanReadTheReport()
    {
        // Regression guard: the header used to end "...,Target,BOM,Result" with two
        // columns both named "BOM", which made PowerShell's Import-Csv fail outright
        // ("The member BOM is already present") - the report's main consumer on Windows.
        string csv = ConversionReport.ToCsvString(
        [
            Entry(sourceEncoding: "us-ascii", sourceHasBom: false,
                  targetEncoding: "utf-8", targetHasBom: true,
                  result: ConversionRowResult.Converted),
        ]);

        string[] lines = csv.Split(
            [Environment.NewLine],
            StringSplitOptions.RemoveEmptyEntries);

        string[] headers = lines[0].Split(',');

        Assert.Equal(8, headers.Length);
        Assert.Equal(headers.Length, headers.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            ["File", "Encoding", "BOM", "Target", "TargetBOM", "Result",
             "ReasonCode", "Diagnostic"],
            headers);

        // Every column still carries its expected value, positionally.
        string[] values = lines[1].Split(',');
        Assert.Equal(headers.Length, values.Length);
        Assert.Equal("us-ascii", values[1]);   // Encoding  (source)
        Assert.Equal("No", values[2]);         // BOM       (source)
        Assert.Equal("utf-8", values[3]);      // Target
        Assert.Equal("Yes", values[4]);        // TargetBOM      (target)
        Assert.Equal("Converted", values[5]);  // Result
    }

    [Fact]
    public void WriteCsv_FirstLine_IsTheFixedHeaderRow()
    {
        string csv = ConversionReport.ToCsvString([]);

        string firstLine = csv.Split(["\r\n", "\n"], StringSplitOptions.None)[0];
        Assert.Equal(
            "File,Encoding,BOM,Target,TargetBOM,Result,ReasonCode,Diagnostic",
            firstLine);
    }

    [Fact]
    public void WriteCsv_NoEntries_WritesOnlyTheHeader()
    {
        string csv = ConversionReport.ToCsvString([]);

        Assert.Equal(
            "File,Encoding,BOM,Target,TargetBOM,Result,ReasonCode,Diagnostic"
            + Environment.NewLine,
            csv);
    }

    [Fact]
    public void WriteCsv_PlainFields_AreWrittenUnquoted()
    {
        ConversionReportEntry entry = Entry(
            filePath: @"C:\Source\file.txt",
            sourceEncoding: "utf-8",
            sourceHasBom: false,
            targetEncoding: "utf-8-bom",
            targetHasBom: true,
            result: ConversionRowResult.Converted);

        string csv = ConversionReport.ToCsvString([entry]);

        string dataLine = csv.Split(["\r\n", "\n"], StringSplitOptions.None)[1];
        Assert.Equal(@"C:\Source\file.txt,utf-8,No,utf-8-bom,Yes,Converted,,", dataLine);
    }

    [Fact]
    public void WriteCsv_FilePathContainingAComma_IsQuoted()
    {
        ConversionReportEntry entry = Entry(filePath: "C:\\Source\\file, with comma.txt");

        string csv = ConversionReport.ToCsvString([entry]);

        string dataLine = csv.Split(["\r\n", "\n"], StringSplitOptions.None)[1];
        Assert.StartsWith("\"C:\\Source\\file, with comma.txt\",", dataLine);
    }

    [Fact]
    public void WriteCsv_FilePathContainingADoubleQuote_IsQuotedAndTheQuoteIsDoubled()
    {
        ConversionReportEntry entry = Entry(filePath: "C:\\Source\\\"quoted\".txt");

        string csv = ConversionReport.ToCsvString([entry]);

        string dataLine = csv.Split(["\r\n", "\n"], StringSplitOptions.None)[1];
        Assert.StartsWith("\"C:\\Source\\\"\"quoted\"\".txt\",", dataLine);
    }

    [Fact]
    public void WriteCsv_FilePathContainingANewline_IsQuoted()
    {
        ConversionReportEntry entry = Entry(filePath: "C:\\Source\\line1\nline2.txt");

        string csv = ConversionReport.ToCsvString([entry]);

        Assert.Contains("\"C:\\Source\\line1\nline2.txt\",", csv);
    }

    [Fact]
    public void WriteCsv_FilePathContainingACarriageReturn_IsQuoted()
    {
        ConversionReportEntry entry = Entry(filePath: "C:\\Source\\line1\rline2.txt");

        string csv = ConversionReport.ToCsvString([entry]);

        Assert.Contains("\"C:\\Source\\line1\rline2.txt\",", csv);
    }

    [Fact]
    public void WriteCsv_EmptyFilePath_IsWrittenAsAnEmptyUnquotedField()
    {
        ConversionReportEntry entry = Entry(filePath: string.Empty);

        string csv = ConversionReport.ToCsvString([entry]);

        string dataLine = csv.Split(["\r\n", "\n"], StringSplitOptions.None)[1];
        Assert.StartsWith(",utf-8,", dataLine);
    }

    [Fact]
    public void WriteCsv_MultipleEntries_WriteOneLineEach()
    {
        ConversionReportEntry[] entries =
        [
            Entry(filePath: "a.txt"),
            Entry(filePath: "b.txt"),
            Entry(filePath: "c.txt"),
        ];

        string csv = ConversionReport.ToCsvString(entries);

        string[] lines = csv.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length); // header + 3 rows
        Assert.StartsWith("a.txt,", lines[1]);
        Assert.StartsWith("b.txt,", lines[2]);
        Assert.StartsWith("c.txt,", lines[3]);
    }

    [Theory]
    [InlineData("Unchanged")]
    [InlineData("Converted")]
    [InlineData("Invalid")]
    [InlineData("Refused")]
    [InlineData("Error")]
    public void WriteCsv_ResultColumn_UsesTheEnumNameVerbatim(string resultName)
    {
        // ConversionRowResult is internal, so a public [Theory] method can't take it as a
        // parameter (CS0051) even with InternalsVisibleTo; round-trip through its name instead.
        var result = Enum.Parse<ConversionRowResult>(resultName);
        ConversionReportEntry entry = Entry(result: result);

        string csv = ConversionReport.ToCsvString([entry]);

        string dataLine = csv.Split(["\r\n", "\n"], StringSplitOptions.None)[1];
        Assert.Equal(resultName, dataLine.Split(',')[5]);
    }

    [Fact]
    public void WriteCsv_ReasonCodeAndDiagnostic_AreIncludedAndEscaped()
    {
        ConversionReportEntry entry = Entry(result: ConversionRowResult.Error);
        entry.ReasonCode = "SourceReadError";
        entry.Diagnostic = "Access denied, retry later.";

        string csv = ConversionReport.ToCsvString([entry]);

        Assert.Contains(",Error,SourceReadError,\"Access denied, retry later.\"", csv);
    }

    [Fact]
    public void WriteCsv_NullWriter_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ConversionReport.WriteCsv([], null!));
    }

    [Fact]
    public void WriteCsv_NullEntries_ThrowsArgumentNullException()
    {
        using var writer = new StringWriter();

        Assert.Throws<ArgumentNullException>(() => ConversionReport.WriteCsv(null!, writer));
    }
}
