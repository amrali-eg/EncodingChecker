using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// EncodingConversionMatrixTests covers the Unicode families exhaustively but contains no
/// legacy multibyte cases, even though Shift-JIS, GB18030, Big5, EUC-JP and EUC-KR are all
/// in the supported set. These are the structural cases for those families: unlike
/// single-byte code pages, a multibyte decoder can desynchronise mid-character, so a
/// byte-exact round trip is the property worth pinning.
///
/// Deliberately not a full matrix expansion - one representative structural case per
/// family, using real script content rather than ASCII.
/// </summary>
public sealed class MultibyteRoundTripTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_multibyte_").FullName;

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

    public static IEnumerable<object[]> MultibyteCases()
    {
        // (charset, representative text in that script)
        yield return ["shift_jis", "こんにちは世界。日本語のテキストです。\r\n二行目。\r\n"];
        yield return ["euc-jp", "こんにちは世界。日本語のテキストです。\r\n二行目。\r\n"];
        yield return ["gb18030", "你好世界。这是一段简体中文文本。\r\n第二行。\r\n"];
        yield return ["big5", "你好世界。這是一段繁體中文文字。\r\n第二行。\r\n"];
        yield return ["euc-kr", "안녕하세요 세계. 한국어 텍스트입니다.\r\n두 번째 줄.\r\n"];
    }

    private static ConversionReportEntry Entry(string path, string sourceCharset) => new()
    {
        FilePath = path,
        SourceEncoding = sourceCharset,
        SourceHasBom = false,
        TargetEncoding = sourceCharset,
        TargetHasBom = false,
    };

    private static ConversionReportEntry Convert(
        ConversionReportEntry entry,
        string targetCharset,
        bool targetWriteBom)
    {
        var completed = new EntrySink();

        ScanEngine.ConvertFiles(
            [entry],
            targetCharset,
            targetWriteBom,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false,
            backup: false,
            completed.Add,
            CancellationToken.None);

        return Assert.Single(completed);
    }

    [Theory]
    [MemberData(nameof(MultibyteCases))]
    public void TheSampleIsActuallyRepresentableInItsEncoding(string charset, string text)
    {
        // Guards the fixtures themselves: a sample the encoder cannot represent would
        // make every other case in this file fail for the wrong reason.
        Encoding encoding = Encoding.GetEncoding(charset);
        byte[] bytes = encoding.GetBytes(text);

        Assert.Equal(text, encoding.GetString(bytes));
        Assert.True(bytes.Length > text.Length, "Expected multibyte expansion.");
        Assert.Empty(encoding.GetPreamble());
    }

    [Theory]
    [MemberData(nameof(MultibyteCases))]
    public void LegacyToUtf8Bom_PreservesContentExactly(string charset, string text)
    {
        Encoding encoding = Encoding.GetEncoding(charset);

        string path = Path.Combine(_root, $"{charset}_to_utf8.txt");
        File.WriteAllBytes(path, encoding.GetBytes(text));

        ConversionReportEntry result = Convert(Entry(path, charset), "utf-8", targetWriteBom: true);

        Assert.Equal(ConversionRowResult.Converted, result.Result);

        byte[] converted = File.ReadAllBytes(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], converted[..3]);
        Assert.Equal(text, new UTF8Encoding(true).GetString(converted[3..]));
    }

    [Theory]
    [MemberData(nameof(MultibyteCases))]
    public void LegacyToUtf8AndBack_IsByteIdenticalToTheOriginal(string charset, string text)
    {
        // The property that matters for a multibyte codec: a full round trip must not
        // desynchronise or substitute a single byte.
        Encoding encoding = Encoding.GetEncoding(charset);

        string path = Path.Combine(_root, $"{charset}_roundtrip.txt");
        byte[] originalBytes = encoding.GetBytes(text);
        File.WriteAllBytes(path, originalBytes);

        ConversionReportEntry entry = Entry(path, charset);

        Assert.Equal(ConversionRowResult.Converted, Convert(entry, "utf-8", false).Result);
        Assert.Equal(text, new UTF8Encoding(false).GetString(File.ReadAllBytes(path)));

        Assert.Equal(ConversionRowResult.Converted, Convert(entry, charset, false).Result);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
    }

    [Theory]
    [MemberData(nameof(MultibyteCases))]
    public void ConvertingToTheEncodingItAlreadyHas_ReportsUnchanged(string charset, string text)
    {
        Encoding encoding = Encoding.GetEncoding(charset);

        string path = Path.Combine(_root, $"{charset}_idempotent.txt");
        byte[] originalBytes = encoding.GetBytes(text);
        File.WriteAllBytes(path, originalBytes);

        ConversionReportEntry entry = Entry(path, charset);

        Assert.Equal(ConversionRowResult.Unchanged, Convert(entry, charset, false).Result);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));

        // And a repeat run stays a no-op rather than rewriting the file.
        Assert.Equal(ConversionRowResult.Unchanged, Convert(entry, charset, false).Result);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
    }

    [Theory]
    [MemberData(nameof(MultibyteCases))]
    public void RequestingABomForAnEncodingWithoutOne_IsRejectedBeforeAnyWrite(
        string charset,
        string text)
    {
        Encoding encoding = Encoding.GetEncoding(charset);

        string path = Path.Combine(_root, $"{charset}_bom.txt");
        File.WriteAllText(path, text, new UTF8Encoding(false));
        byte[] originalBytes = File.ReadAllBytes(path);

        ConversionResult result = EncodingConverter.Convert(
            path, path,
            new UTF8Encoding(false),
            encoding,
            new ConversionOptions { WriteBom = true },
            progress: null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.BomMismatch, result.ErrorCode);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
    }

    // GB18030 is deliberately absent: unlike the others it maps the whole Unicode range,
    // so an emoji is representable and conversion correctly succeeds. See
    // Gb18030_CoversTheFullUnicodeRange below.
    public static IEnumerable<object[]> UnicodeIncompleteCases() =>
        MultibyteCases().Where(c => (string)c[0] != "gb18030");

    [Theory]
    [MemberData(nameof(UnicodeIncompleteCases))]
    public void UnrepresentableContent_IsRejectedRatherThanSubstituted(
        string charset,
        string text)
    {
        _ = text;

        // These code pages cannot represent an astral-plane emoji. The encoder must fail
        // rather than silently writing '?' - the substitution the hash check exists to catch.
        string path = Path.Combine(_root, $"{charset}_unmappable.txt");
        File.WriteAllText(path, "before 🌍 after", new UTF8Encoding(false));
        byte[] originalBytes = File.ReadAllBytes(path);

        ConversionReportEntry result = Convert(
            Entry(path, "utf-8"), charset, targetWriteBom: false);

        Assert.Equal(ConversionRowResult.Error, result.Result);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Gb18030_CoversTheFullUnicodeRange()
    {
        // GB18030 is the one legacy code page here that is Unicode-complete, so content
        // the others reject converts cleanly and round-trips exactly.
        const string text = "汉字 with emoji 🌍 and 𠜎 supplementary\r\n";

        string path = Path.Combine(_root, "gb18030_full_unicode.txt");
        File.WriteAllText(path, text, new UTF8Encoding(false));

        ConversionReportEntry entry = Entry(path, "utf-8");

        Assert.Equal(ConversionRowResult.Converted, Convert(entry, "gb18030", false).Result);
        Assert.Equal(text, Encoding.GetEncoding("gb18030").GetString(File.ReadAllBytes(path)));

        Assert.Equal(ConversionRowResult.Converted, Convert(entry, "utf-8", false).Result);
        Assert.Equal(text, new UTF8Encoding(false).GetString(File.ReadAllBytes(path)));
    }
}
