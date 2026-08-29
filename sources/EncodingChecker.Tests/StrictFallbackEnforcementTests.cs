using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// Pins the strict-fallback contract at the point where it is easy to lose.
///
/// Assigning <see cref="Decoder.Fallback"/> or <see cref="Encoder.Fallback"/> after
/// <see cref="Encoding.GetDecoder"/>/<see cref="Encoding.GetEncoder"/> compiles, reads as
/// correct, and does nothing for the encodings that come from
/// <see cref="CodePagesEncodingProvider"/>: those codecs capture their fallbacks from the
/// parent <see cref="Encoding"/> when they are created. A build that regressed to that
/// pattern would silently substitute characters for input its own codec cannot represent,
/// and the conversion would still be reported as a success - the content digest compares
/// decoded source against decoded target, so both sides pass through the same lossy
/// decoder and agree.
///
/// The first two tests document the platform behaviour the fix exists for, so a future
/// reader can see that the indirection in MakeStrictEncoding is load-bearing rather than
/// ceremonial. The rest pin EC's own observable behaviour.
/// </summary>
public sealed class StrictFallbackEnforcementTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_strictfallback_").FullName;

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

    // EUC-JP bytes whose second character is a JIS X 0212 sequence introduced by SS3
    // (0x8F). Python's euc_jp codec maps it; .NET's code page 51932 does not, so a
    // correctly strict decoder must reject these bytes rather than substitute.
    private static readonly byte[] JisX0212Bytes =
        [0x8F, 0xB0, 0xDF, 0xB9, 0xA5, 0xA1, 0xA4, 0xC0, 0xA4, 0xB3, 0xA6, 0xA1, 0xAA];

    [Fact]
    public void AssigningDecoderFallbackAfterConstruction_DoesNotTakeEffect()
    {
        Encoding encoding = Encoding.GetEncoding("euc-jp");

        Decoder decoder = encoding.GetDecoder();
        decoder.Fallback = DecoderFallback.ExceptionFallback;

        char[] buffer = new char[encoding.GetMaxCharCount(JisX0212Bytes.Length)];

        // No exception: the assignment above was silently ignored. This is the platform
        // behaviour, asserted so that a change in it is caught here rather than in the
        // field.
        int written = decoder.GetChars(
            JisX0212Bytes, 0, JisX0212Bytes.Length, buffer, 0, flush: true);

        Assert.True(written > 0);
    }

    [Fact]
    public void SupplyingFallbacksToGetEncoding_DoesTakeEffect()
    {
        Encoding strict = Encoding.GetEncoding(
            Encoding.GetEncoding("euc-jp").CodePage,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

        Assert.Throws<DecoderFallbackException>(() => strict.GetString(JisX0212Bytes));
    }

    [Fact]
    public void ConvertingUndecodableBytes_FailsInsteadOfSubstituting()
    {
        string path = Path.Combine(_root, "jisx0212.txt");
        File.WriteAllBytes(path, JisX0212Bytes);

        ConversionReportEntry result = Convert(path, "euc-jp", "utf-8");

        // The bytes cannot be represented by the codec EC was told to use. Refusing is
        // the only correct outcome; substituting would lose content silently.
        Assert.Equal(ConversionRowResult.Error, result.Result);

        // A failed conversion must leave the file exactly as it was.
        Assert.Equal(JisX0212Bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void ConvertingUnencodableCharacters_FailsInsteadOfSubstituting()
    {
        // "café" has no representation in EUC-JP; the encoder side of the same defect
        // turned it into "cafe" with no error.
        const string text = "café";

        string path = Path.Combine(_root, "unencodable.txt");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));

        ConversionReportEntry result = Convert(path, "utf-8", "euc-jp");

        Assert.Equal(ConversionRowResult.Error, result.Result);
        Assert.Equal(text, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void TextValidation_RejectsBytesTheEncodingCannotRepresent()
    {
        // IsValidText is the gate that independently validates UtfUnknown's answer, and
        // the encodings it is asked about are exactly the ones where assigning
        // Decoder.Fallback does nothing. Unfixed, the decode substitutes, the substituted
        // characters still look like text, and the gate confirms a codec that cannot read
        // the file.
        Encoding eucJp = Encoding.GetEncoding("euc-jp");

        Assert.False(TextValidation.IsValidText(eucJp, JisX0212Bytes));
    }

    [Fact]
    public void TextValidation_StillAcceptsContentTheEncodingCanRepresent()
    {
        Encoding eucJp = Encoding.GetEncoding("euc-jp");
        byte[] bytes = eucJp.GetBytes("こんにちは世界。日本語のテキストです。");

        Assert.True(TextValidation.IsValidText(eucJp, bytes));
    }

    [Fact]
    public void TextEncodingStrict_LeavesAnAlreadyStrictEncodingAlone()
    {
        Encoding strict = Encoding.GetEncoding(
            "euc-jp", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        Assert.Same(strict, TextEncoding.Strict(strict));
    }

    [Fact]
    public void TextEncodingStrict_RefusesAnEncodingThatCannotBeRebuiltStrictly()
    {
        // Returning the original encoding would reintroduce the code-page fallback bug.
        Encoding unrebuildable = new UnrebuildableEncoding();

        Assert.Throws<NotSupportedException>(() => TextEncoding.Strict(unrebuildable));
    }

    private sealed class UnrebuildableEncoding : Encoding
    {
        public override int CodePage => 65_000_002;

        public override int GetByteCount(char[] chars, int index, int count) => count;

        public override int GetBytes(
            char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) => 0;

        public override int GetCharCount(byte[] bytes, int index, int count) => count;

        public override int GetChars(
            byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) => 0;

        public override int GetMaxByteCount(int charCount) => charCount;

        public override int GetMaxCharCount(int byteCount) => byteCount;
    }

    [Theory]
    [InlineData("shift_jis")]
    [InlineData("euc-jp")]
    [InlineData("big5")]
    [InlineData("gb18030")]
    [InlineData("euc-kr")]
    [InlineData("utf-8")]
    [InlineData("us-ascii")]
    public void EveryConvertibleEncoding_ExposesStrictCodecs(string charset)
    {
        // The property the conversion pipeline depends on, checked directly for each
        // supported family rather than only through a file round trip.
        Encoding encoding = Encoding.GetEncoding(charset);

        Encoding strict = Encoding.GetEncoding(
            encoding.CodePage,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

        Assert.IsType<DecoderExceptionFallback>(strict.DecoderFallback);
        Assert.IsType<EncoderExceptionFallback>(strict.EncoderFallback);
    }

    [Fact]
    public void ValidContentStillConvertsAfterTheFix()
    {
        // The fix must tighten only the invalid cases: a representable document has to
        // keep converting, byte for byte.
        const string text = "こんにちは世界。日本語のテキストです。\r\n";

        Encoding eucJp = Encoding.GetEncoding("euc-jp");
        string path = Path.Combine(_root, "valid.txt");
        File.WriteAllBytes(path, eucJp.GetBytes(text));

        ConversionReportEntry result = Convert(path, "euc-jp", "utf-8");

        Assert.Equal(ConversionRowResult.Converted, result.Result);
        Assert.Equal(text, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    private static ConversionReportEntry Convert(
        string path,
        string sourceCharset,
        string targetCharset)
    {
        var entry = new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = sourceCharset,
            SourceHasBom = false,
            TargetEncoding = sourceCharset,
            TargetHasBom = false,
            SourceEncodingWasSpecified = true,
        };

        var completed = new EntrySink();

        ScanEngine.ConvertFiles(
            [entry],
            targetCharset,
            targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false,
            backup: false,
            completed.Add,
            CancellationToken.None);

        return Assert.Single(completed);
    }
}
