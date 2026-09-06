using System.Text;
using System.Threading;

namespace EncodingChecker.Tests;

/// <summary>
/// Every way <c>-Validate</c> can reject a file has to say which way it was.
/// </summary>
/// <remarks>
/// There are four. Two explained themselves — a file whose whole contents fail strict
/// decoding, and one whose BOM-less byte order cannot be proven. The other two arrived as a
/// bare <c>Invalid</c> with an empty reason column, and they are not the same situation: a
/// charset the caller did not allow means widen the list or convert the file, while an
/// unidentifiable one means EC could not tell what it is. That made <c>-Validate</c> the
/// only outcome in the product whose reason the reader had to reconstruct, from the encoding
/// column and the list they had passed in.
/// </remarks>
public sealed class ValidateReasonCodeTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec-validate-").FullName;

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

    private ConversionReportEntry Validate(byte[] contents, params string[] allowed)
    {
        File.WriteAllBytes(Path.Combine(_root, "f.txt"), contents);

        var entries = new EntrySink();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                Action = ScanAction.Validate,
                ValidCharsets = allowed,
                MaxParallelism = 1,
            },
            entries.Add,
            CancellationToken.None);

        return entries.Single();
    }

    private static byte[] Cjk(int repeats) =>
        new UTF8Encoding(false).GetBytes(
            string.Concat(Enumerable.Repeat("中文内容测试", repeats)));

    [Fact]
    public void AnAllowedCharsetThatDecodesCleanlyPasses()
    {
        ConversionReportEntry entry = Validate(
            new UTF8Encoding(false).GetBytes("plain ascii content\n"), "us-ascii", "utf-8");

        Assert.Equal(ConversionRowResult.Unchanged, entry.Result);
        Assert.Null(entry.ReasonCode);
    }

    [Fact]
    public void ACharsetOutsideTheAllowedListSaysSo()
    {
        ConversionReportEntry entry = Validate(
            Encoding.GetEncoding("windows-1251").GetBytes("Здравствуй, мир! Это русский текст.\n"),
            "us-ascii", "utf-8");

        Assert.Equal(ConversionRowResult.Invalid, entry.Result);
        Assert.Equal(ConversionReasonCodes.CharsetNotAllowed, entry.ReasonCode);
        Assert.Contains("windows-1251", entry.Diagnostic);
        Assert.Contains("not in the allowed list", entry.Diagnostic);
    }

    [Fact]
    public void AFileEcCannotIdentifyIsNamedAsThatInstead()
    {
        // The distinction the empty reason column erased: this is not "you did not allow
        // it", and -DetectOnly already calls the same condition UnknownEncoding.
        ConversionReportEntry entry = Validate([], "us-ascii", "utf-8");

        Assert.Equal(ConversionRowResult.Invalid, entry.Result);
        Assert.Equal(ConversionReasonCodes.UnknownEncoding, entry.ReasonCode);
        Assert.NotEqual(ConversionReasonCodes.CharsetNotAllowed, entry.ReasonCode);
    }

    [Fact]
    public void AnAllowedCharsetThatFailsStrictDecodingKeepsItsOwnReason()
    {
        ConversionReportEntry entry = Validate([.. Cjk(3), 0xE4, 0xB8], "utf-8");

        Assert.Equal(ConversionRowResult.Invalid, entry.Result);
        Assert.Equal(ConversionReasonCodes.StrictValidationFailed, entry.ReasonCode);
        Assert.Contains("E4", entry.Diagnostic);
    }

    [Fact]
    public void AnAllowedCharsetWithAnUnprovableByteOrderKeepsItsOwnReason()
    {
        byte[] utf16 = new UnicodeEncoding(false, false).GetBytes(
            string.Concat(Enumerable.Repeat("a quiet line of words\n", 4)));

        ConversionReportEntry entry = Validate(utf16, "utf-16");

        Assert.Equal(ConversionRowResult.Invalid, entry.Result);
        Assert.Equal(ConversionReasonCodes.AmbiguousBomlessUtf16, entry.ReasonCode);
    }

    [Fact]
    public void NoRejectionLeavesTheReaderWithoutAReason()
    {
        // The property, rather than the four cases: nothing -Validate rejects may arrive
        // as a bare Invalid again.
        (byte[] Contents, string[] Allowed)[] rejected =
        [
            (Encoding.GetEncoding("windows-1251").GetBytes("Здравствуй, мир!\n"), ["utf-8"]),
            ([], ["utf-8"]),
            ([.. Cjk(3), 0xE4, 0xB8], ["utf-8"]),
            (new UnicodeEncoding(false, false).GetBytes(
                 string.Concat(Enumerable.Repeat("a quiet line of words\n", 4))), ["utf-16"]),
        ];

        foreach ((byte[] contents, string[] allowed) in rejected)
        {
            ConversionReportEntry entry = Validate(contents, allowed);

            Assert.Equal(ConversionRowResult.Invalid, entry.Result);
            Assert.False(
                string.IsNullOrEmpty(entry.ReasonCode),
                $"a rejected file carried no reason code (encoding was {entry.SourceEncoding})");
            Assert.False(
                string.IsNullOrEmpty(entry.Diagnostic),
                $"a rejected file carried no diagnostic (encoding was {entry.SourceEncoding})");
        }
    }
}
