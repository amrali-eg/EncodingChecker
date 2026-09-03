using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// Every mode must agree about whether a BOM-less UTF-16 byte order can be established.
/// </summary>
/// <remarks>
/// Ambiguity was computed only for <see cref="ScanAction.Convert"/>, so the same bytes
/// got three different confidences from one build: <c>-DetectOnly</c> reported utf-16
/// with no caveat and exit 0, <c>-Validate "utf-16"</c> reported the file valid and
/// exit 0, and <c>-Target</c> refused it with exit 5. The validate row is the one that
/// mattered - a CI gate went green on a file EC had already decided it could not read
/// safely, and a later conversion of that same tree halts.
/// <para>
/// Byte-swapped Latin text lands in the CJK range, so both byte orders decode strictly
/// and the file cannot say which it is. That is a fact about the bytes, not a property
/// of conversion, so it now holds in every mode.
/// </para>
/// </remarks>
public sealed class ReadOnlyModeAmbiguityTests : IDisposable
{
    private const int ExpectedClean = 0;
    private const int ExpectedChangesNeeded = 2;
    private const int ExpectedSafeRefusal = 5;

    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_ambiguity_modes_").FullName;

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

    private static int Run(params string[] args)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;

        try
        {
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());

            return Program.RunConsoleMode(args);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    /// <summary>Latin text in BOM-less UTF-16LE, which decodes equally well as BE.</summary>
    private string WriteAmbiguous(string name = "ambiguous.txt")
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(
            path,
            new UnicodeEncoding(bigEndian: false, byteOrderMark: false)
                .GetBytes("Hello World, this is plain text."));

        return path;
    }

    /// <summary>BOM-less UTF-16LE whose opposite order is structurally impossible.</summary>
    private string WriteProvable(string name = "provable.txt")
    {
        string path = Path.Combine(_root, name);

        // U+00D8 is stored little-endian as D8 00, which read big-endian is an unpaired
        // high surrogate, so strict UTF-16BE decoding rejects the file.
        //
        // A supplementary-plane character does NOT work here, which is worth recording:
        // byte-swapping the surrogate pair D83D DE00 yields 3DD8 and 00DE, both ordinary
        // scalars, so such a file remains ambiguous.
        File.WriteAllBytes(
            path,
            new UnicodeEncoding(bigEndian: false, byteOrderMark: false)
                .GetBytes("Hello Ø World, plain text here now ok."));

        return path;
    }

    private List<ConversionReportEntry> Scan(ScanAction action, params string[] validCharsets)
    {
        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = action,
            ValidCharsets = validCharsets.Length == 0 ? null : validCharsets,
            TargetCharset = action == ScanAction.Convert ? "utf-8" : null,
            WhatIf = action == ScanAction.Convert,
        };

        var sink = new EntrySink();
        ScanEngine.ScanDirectory(options, sink.Add, CancellationToken.None);

        return sink.ToList();
    }

    [Fact]
    public void DetectOnly_ReportsThatTheByteOrderIsAnEstimate()
    {
        WriteAmbiguous();

        ConversionReportEntry entry = Assert.Single(Scan(ScanAction.Detect));

        Assert.Equal("utf-16", entry.SourceEncoding);

        // Nothing failed and nothing was withheld, so the result stays Unchanged; the
        // row has to say which of the two readings it is reporting.
        Assert.Equal(ConversionRowResult.Unchanged, entry.Result);
        Assert.Equal(ConversionReasonCodes.AmbiguousBomlessUtf16, entry.ReasonCode);
        Assert.Contains("estimate", entry.Diagnostic);

        // The refusal wording would be false here: nothing was going to be converted.
        Assert.DoesNotContain("no conversion was performed", entry.Diagnostic);
    }

    [Fact]
    public void Validate_DoesNotPassAFileWhoseIdentityItCannotEstablish()
    {
        WriteAmbiguous();

        ConversionReportEntry entry = Assert.Single(Scan(ScanAction.Validate, "utf-16"));

        Assert.Equal(ConversionRowResult.Invalid, entry.Result);
        Assert.Equal(ConversionReasonCodes.AmbiguousBomlessUtf16, entry.ReasonCode);

        // The reason must not read as "wrong encoding": the label was allowed, and it is
        // the file's membership of it that cannot be confirmed.
        Assert.Contains("is in the allowed list", entry.Diagnostic);
    }

    [Fact]
    public void EveryModeAgreesAboutTheSameBytes()
    {
        // The point of the fix. Before it, these three lines disagreed.
        string path = WriteAmbiguous();
        byte[] original = File.ReadAllBytes(path);

        Assert.Equal(ExpectedClean, Run("-BasePath", _root, "-DetectOnly"));
        Assert.Equal(
            ExpectedChangesNeeded,
            Run("-BasePath", _root, "-Validate", "utf-16", "-FailOnChanges", "-Quiet"));
        Assert.Equal(
            ExpectedSafeRefusal,
            Run("-BasePath", _root, "-Target", "utf-8", "-WhatIf", "-Quiet"));

        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void AProvableByteOrderStillPassesEveryMode()
    {
        // The control. A check that flagged all BOM-less UTF-16 would satisfy the tests
        // above while making -Validate useless for the encoding it exists to check.
        WriteProvable();

        ConversionReportEntry detected = Assert.Single(Scan(ScanAction.Detect));

        Assert.Equal("utf-16", detected.SourceEncoding);
        Assert.Null(detected.ReasonCode);
        Assert.False(detected.HasAmbiguousBomlessUtf16);

        ConversionReportEntry validated =
            Assert.Single(Scan(ScanAction.Validate, "utf-16"));

        Assert.Equal(ConversionRowResult.Unchanged, validated.Result);
        Assert.Null(validated.ReasonCode);

        ConversionReportEntry converted = Assert.Single(Scan(ScanAction.Convert));

        Assert.Equal(PlannedAction.Convert, converted.Action);
    }

    [Fact]
    public void AFileWithAByteOrderMarkIsNeverAmbiguous()
    {
        // A BOM settles the question, which is exactly the remedy the diagnostic offers.
        //
        // Encoding.GetBytes never emits a preamble whatever the constructor was given,
        // so the marker is written explicitly. Relying on byteOrderMark: true produces a
        // BOM-less file, and the test then asserts the opposite of what it claims.
        var utf16 = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        string path = Path.Combine(_root, "withbom.txt");
        File.WriteAllBytes(
            path,
            [
                .. utf16.GetPreamble(),
                .. utf16.GetBytes("Hello World, this is plain text."),
            ]);

        ConversionReportEntry entry = Assert.Single(Scan(ScanAction.Detect));

        Assert.False(entry.HasAmbiguousBomlessUtf16);
        Assert.Null(entry.ReasonCode);
    }

    [Fact]
    public void NonUtf16FilesAreUnaffected()
    {
        // The ambiguity probe is guarded by code page before it opens anything, so the
        // ordinary case must not acquire a reason code or a second read.
        File.WriteAllText(Path.Combine(_root, "plain.txt"), "just ascii here");

        ConversionReportEntry entry = Assert.Single(Scan(ScanAction.Detect));

        Assert.Equal("us-ascii", entry.SourceEncoding);
        Assert.False(entry.HasAmbiguousBomlessUtf16);
        Assert.Null(entry.ReasonCode);
    }
}
