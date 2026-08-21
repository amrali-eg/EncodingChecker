using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// Exercises <see cref="EncodingConverter.Convert"/> directly: valid round-trips through the
/// SHA-256 verification path, and unrepresentable content - both when the BCL encoder
/// throws, and the documented case where it silently substitutes '?' instead.
/// </summary>
public sealed class EncodingConverterRobustnessTests : IDisposable
{
    private readonly string _root;

    public EncodingConverterRobustnessTests()
    {
        _root = Directory.CreateTempSubdirectory("ec_converter_").FullName;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
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
    public void Convert_TargetEncoderSilentlySubstitutes_HashVerificationCatchesCorruptionAndLeavesFileUnchanged(
        string unmappableContent)
    {
        // windows-1252 silently writes '?' here instead of throwing (verified empirically;
        // see MakeStrictEncoder's remarks). SHA-256 verification is the real safety net.
        string path = WriteFile("unmappable.txt", unmappableContent, new UTF8Encoding(false));
        byte[] originalBytes = File.ReadAllBytes(path);

        ConversionResult result = EncodingConverter.Convert(
            path, path, Encoding.UTF8, Encoding.GetEncoding("windows-1252"), new ConversionOptions());

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.UnicodeMismatch, result.ErrorCode);
        Assert.False(result.VerificationPassed);

        // A rejected conversion must never install corrupted content over the original.
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
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
