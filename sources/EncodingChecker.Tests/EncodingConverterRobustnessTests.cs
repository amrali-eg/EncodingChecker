using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// Exercises <see cref="EncodingConverter.Convert"/> directly: valid round-trips through the
/// SHA-256 verification path, and unrepresentable content - both when the encoder throws,
/// and the residual case where an encoding cannot be rebuilt strictly and substitutes
/// instead, which the digest comparison has to catch.
/// </summary>
public sealed class EncodingConverterRobustnessTests : IDisposable
{
    private readonly string _root;

    public EncodingConverterRobustnessTests()
    {
        _root = Directory.CreateTempSubdirectory("ec_converter_").FullName;
    }

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

    private string WriteFile(string name, string content, Encoding encoding)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content, encoding);
        return path;
    }

    [Fact]
    public void Convert_ValidMultilingualRoundTrip_PassesHashVerificationWithCorrectScalarCount()
    {
        string path = WriteFile("valid.txt", TestContent.Multilingual, new UTF8Encoding(false));
        int expectedScalars = TestContent.Multilingual.EnumerateRunes().Count();

        Encoding target = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        ConversionResult result = EncodingConverter.Convert(
            path, path, Encoding.UTF8, target, new ConversionOptions { WriteBom = true });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(result.VerificationPassed);
        Assert.True(result.BomVerificationPassed);
        Assert.Equal(expectedScalars, result.UnicodeScalarsVerified);
        Assert.Equal(ConversionErrorCode.None, result.ErrorCode);

        string decoded = target.GetString(File.ReadAllBytes(path)[target.GetPreamble().Length..]);
        Assert.Equal(TestContent.Multilingual, decoded);
    }

    [Fact]
    public void Convert_EmojiSurrogatePair_CountsAsOneScalarAndRoundTrips()
    {
        const string content = "before \U0001F30D after"; // U+1F30D EARTH GLOBE, a surrogate pair in UTF-16
        string path = WriteFile("emoji.txt", content, new UTF8Encoding(false));

        ConversionResult result = EncodingConverter.Convert(
            path, path, Encoding.UTF8, new UTF8Encoding(true), new ConversionOptions { WriteBom = true });

        Assert.True(result.Success, result.ErrorMessage);
        // "before " (7) + globe (1 scalar) + " after" (6) = 14 scalars.
        Assert.Equal(14, result.UnicodeScalarsVerified);
    }

    [Fact]
    public void Convert_TargetCannotRepresentContentAndEncoderThrows_ReturnsEncodeErrorAndLeavesFileUnchanged()
    {
        // us-ascii's encoder honors ExceptionFallback and throws for non-ASCII scalars.
        string path = WriteFile("cjk.txt", "世界", new UTF8Encoding(false));
        byte[] originalBytes = File.ReadAllBytes(path);

        ConversionResult result = EncodingConverter.Convert(
            path, path, Encoding.UTF8, Encoding.ASCII, new ConversionOptions());

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.TargetEncodeError, result.ErrorCode);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("世界")]        // CJK
    [InlineData("مرحبا")]       // Arabic
    [InlineData("Привет")]      // Cyrillic
    public void Convert_TargetCannotRepresentContent_FailsAtEncodeAndLeavesFileUnchanged(
        string unmappableContent)
    {
        // This case used to reach the SHA-256 backstop as UnicodeMismatch: windows-1252's
        // encoder substituted '?' because assigning Encoder.Fallback after GetEncoder()
        // has no effect for CodePagesEncodingProvider encodings. MakeStrictEncoding now
        // supplies the fallback up front, so the loss is refused where it happens and is
        // reported as what it is - see StrictFallbackEnforcementTests.
        string path = WriteFile("unmappable.txt", unmappableContent, new UTF8Encoding(false));
        byte[] originalBytes = File.ReadAllBytes(path);

        ConversionResult result = EncodingConverter.Convert(
            path, path, Encoding.UTF8, Encoding.GetEncoding("windows-1252"), new ConversionOptions());

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.TargetEncodeError, result.ErrorCode);

        // A rejected conversion must never install corrupted content over the original.
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Convert_EncodingThatCannotBeRebuiltStrictly_IsCaughtByHashVerification()
    {
        // MakeStrictEncoding rebuilds an encoding from its code page to make the fallbacks
        // stick, and documents that an encoding it cannot rebuild keeps its original
        // codecs - leaving the SHA-256 comparison as the backstop. That path is otherwise
        // unreachable through the BCL encodings, so it is pinned with an encoding whose
        // code page does not exist and whose encoder substitutes rather than throws.
        string path = WriteFile("substituting.txt", "Привет", new UTF8Encoding(false));
        byte[] originalBytes = File.ReadAllBytes(path);

        ConversionResult result = EncodingConverter.Convert(
            path, path, Encoding.UTF8, new SubstitutingEncoding(), new ConversionOptions());

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.UnicodeMismatch, result.ErrorCode);
        Assert.False(result.VerificationPassed);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
    }

    /// <summary>
    /// A single-byte encoding that replaces anything non-ASCII with '?' and reports a code
    /// page that is not registered, so it cannot be reconstructed with strict fallbacks.
    /// </summary>
    private sealed class SubstitutingEncoding : Encoding
    {
        // Not a real code page; Encoding.GetEncoding must fail for it.
        public override int CodePage => 65_000_001;

        public override int GetByteCount(char[] chars, int index, int count) => count;

        public override int GetBytes(
            char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        {
            for (int i = 0; i < charCount; i++)
            {
                char c = chars[charIndex + i];
                bytes[byteIndex + i] = c < 0x80 ? (byte)c : (byte)'?';
            }

            return charCount;
        }

        public override int GetCharCount(byte[] bytes, int index, int count) => count;

        public override int GetChars(
            byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        {
            for (int i = 0; i < byteCount; i++)
            {
                chars[charIndex + i] = (char)bytes[byteIndex + i];
            }

            return byteCount;
        }

        public override int GetMaxByteCount(int charCount) => charCount;

        public override int GetMaxCharCount(int byteCount) => byteCount;
    }

    [Fact]
    public void Convert_TargetEncoderCanRepresentContent_SucceedsEvenForNonAsciiTarget()
    {
        // 'é' (U+00E9) is representable in windows-1252, unlike the previous test's scripts.
        string path = WriteFile("cafe.txt", "café", new UTF8Encoding(false));

        ConversionResult result = EncodingConverter.Convert(
            path, path, Encoding.UTF8, Encoding.GetEncoding("windows-1252"), new ConversionOptions());

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(result.VerificationPassed);

        string decoded = Encoding.GetEncoding("windows-1252").GetString(File.ReadAllBytes(path));
        Assert.Equal("café", decoded);
    }

    [Fact]
    public void Convert_DeclaredSourceEncodingDoesNotMatchActualBytes_ReturnsDecodeError()
    {
        // Real UTF-16LE bytes, decoded as UTF-8 - must fail cleanly, not decode garbage.
        string path = Path.Combine(_root, "mismatched.txt");
        File.WriteAllBytes(path, Encoding.Unicode.GetBytes(TestContent.Multilingual));
        byte[] originalBytes = File.ReadAllBytes(path);

        ConversionResult result = EncodingConverter.Convert(
            path, path, Encoding.UTF8, new UTF8Encoding(true), new ConversionOptions { WriteBom = true });

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.SourceDecodeError, result.ErrorCode);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Convert_RequestingBomForEncodingWithoutOne_FailsBeforeAnyIo()
    {
        string path = WriteFile("noBomTarget.txt", TestContent.Ascii, Encoding.ASCII);
        byte[] originalBytes = File.ReadAllBytes(path);

        // UTF-7 (and plain ASCII) have no byte-order mark to write.
        ConversionResult result = EncodingConverter.Convert(
            path, path, Encoding.ASCII, Encoding.ASCII, new ConversionOptions { WriteBom = true });

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.BomMismatch, result.ErrorCode);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Convert_NonExistentSourceFile_ReturnsSourceOpenErrorInsteadOfThrowing()
    {
        string path = Path.Combine(_root, "does-not-exist.txt");

        ConversionResult result = EncodingConverter.Convert(
            path, path, Encoding.UTF8, new UTF8Encoding(true), new ConversionOptions { WriteBom = true });

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.SourceOpenError, result.ErrorCode);
    }
}
