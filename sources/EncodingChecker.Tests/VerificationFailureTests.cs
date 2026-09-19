using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// Output that is damaged without changing its byte length must still be refused.
/// </summary>
/// <remarks>
/// Verification checks length first, then the byte-order mark, then decoded content.
/// <c>DamagedTemporaryOutput_IsRefusedAndLeavesTheSourceUnchanged</c> appends a byte, so it
/// stops at the length check and never reaches the later two. Each case here keeps the
/// length identical so it gets past that check and lands on exactly one later refusal.
/// A refusal that reports the wrong reason, or that leaves the source altered, is the
/// failure these tests guard against.
/// </remarks>
public sealed class VerificationFailureTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_verify_fail_").FullName;

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

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private string WriteSource(string name, byte[] bytes)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static ConversionResult ConvertFromUtf8(
        string path, Encoding target, bool writeBom, Action<string> damage) =>
        EncodingConverter.Convert(
            path,
            path,
            new UTF8Encoding(false),
            target,
            new ConversionOptions
            {
                WriteBom = writeBom,
                BeforeVerifyTemporaryOutput = damage,
            });

    private void AssertRefusedWithSourceAndDirectoryIntact(
        ConversionResult result, string path, byte[] original)
    {
        Assert.False(result.Success);
        Assert.False(result.ReplacementCommitted);
        Assert.Equal(original, File.ReadAllBytes(path));

        // No temporary output may outlive the refusal.
        Assert.Equal([path], Directory.GetFiles(_root));
    }

    [Fact]
    public void ABomThatWasRequestedButIsMissingFromTheOutput_IsRefused()
    {
        byte[] original = Encoding.UTF8.GetBytes("hello world");
        string path = WriteSource("missing-bom.txt", original);
        byte[]? beforeDamage = null;

        ConversionResult result = ConvertFromUtf8(
            path, new UTF8Encoding(true), writeBom: true, damage: temporaryOutput =>
            {
                // Same length: overwrite the three BOM bytes with spaces. Assertions belong
                // outside this hook, where an exception is reported as an unrelated
                // ConversionErrorCode.Unexpected.
                beforeDamage = File.ReadAllBytes(temporaryOutput);
                byte[] bytes = [.. beforeDamage];
                bytes[0] = bytes[1] = bytes[2] = (byte)' ';
                File.WriteAllBytes(temporaryOutput, bytes);
            });

        Assert.NotNull(beforeDamage);
        Assert.Equal(Utf8Bom, beforeDamage[..3]);

        AssertRefusedWithSourceAndDirectoryIntact(result, path, original);
        Assert.Equal(ConversionErrorCode.BomMismatch, result.ErrorCode);
        Assert.Contains("missing the expected byte-order mark", result.ErrorMessage);
        Assert.False(result.BomVerificationPassed);
    }

    [Fact]
    public void ABomThatWasNotRequestedButAppearsInTheOutput_IsRefused()
    {
        byte[] original = Encoding.UTF8.GetBytes("hello world");
        string path = WriteSource("extra-bom.txt", original);
        byte[]? beforeDamage = null;

        // Encoding.UTF8 has a preamble, so the BOM check runs; WriteBom = false asks for none.
        ConversionResult result = ConvertFromUtf8(
            path, Encoding.UTF8, writeBom: false, damage: temporaryOutput =>
            {
                beforeDamage = File.ReadAllBytes(temporaryOutput);
                byte[] bytes = [.. beforeDamage];
                Utf8Bom.CopyTo(bytes, 0);
                File.WriteAllBytes(temporaryOutput, bytes);
            });

        Assert.NotNull(beforeDamage);
        Assert.NotEqual(Utf8Bom, beforeDamage[..3]);

        AssertRefusedWithSourceAndDirectoryIntact(result, path, original);
        Assert.Equal(ConversionErrorCode.BomMismatch, result.ErrorCode);
        Assert.Contains("unexpectedly contains a byte-order mark", result.ErrorMessage);
        Assert.False(result.BomVerificationPassed);
    }

    [Fact]
    public void OutputWhoseTextDiffersAtTheSameLength_IsRefusedAsAContentMismatch()
    {
        byte[] original = Encoding.UTF8.GetBytes("Hello world");
        string path = WriteSource("same-length.txt", original);

        // A BOM target, so the BOM check runs and passes and BomVerificationPassed says so.
        ConversionResult result = ConvertFromUtf8(
            path, new UTF8Encoding(true), writeBom: true, damage: temporaryOutput =>
            {
                byte[] bytes = File.ReadAllBytes(temporaryOutput);
                bytes[Utf8Bom.Length] = (byte)'J';
                File.WriteAllBytes(temporaryOutput, bytes);
            });

        AssertRefusedWithSourceAndDirectoryIntact(result, path, original);
        Assert.Equal(ConversionErrorCode.UnicodeMismatch, result.ErrorCode);
        Assert.Contains("Decoded content differs from source", result.ErrorMessage);
        Assert.False(result.VerificationPassed);
        Assert.True(result.BomVerificationPassed);
        Assert.Equal("Hello world".Length, result.UnicodeScalarsVerified);
    }

    [Fact]
    public void OutputThatDecodesToADifferentLengthAtTheSameByteCount_NamesTheLengthDifference()
    {
        // Four ASCII bytes are four UTF-16 code units. Four bytes of one emoji are the same
        // byte count but two code units and a single scalar.
        byte[] original = Encoding.UTF8.GetBytes("abcd");
        string path = WriteSource("same-bytes.txt", original);

        ConversionResult result = ConvertFromUtf8(
            path, new UTF8Encoding(true), writeBom: true, damage: temporaryOutput =>
                File.WriteAllBytes(temporaryOutput, [.. Utf8Bom, 0xF0, 0x9F, 0x98, 0x80]));

        AssertRefusedWithSourceAndDirectoryIntact(result, path, original);
        Assert.Equal(ConversionErrorCode.UnicodeMismatch, result.ErrorCode);
        Assert.Contains("Decoded content length differs from source", result.ErrorMessage);
        Assert.Equal(1, result.UnicodeScalarsVerified);
        Assert.False(result.VerificationPassed);
        Assert.True(result.BomVerificationPassed);
    }

    // FF FE opens both the UTF-16LE and the UTF-32LE preamble, so the BOM check has to compare
    // the whole preamble for the target's own width, not just its first bytes.
    private static Encoding WideTarget(string name) => name switch
    {
        "utf16le" => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
        "utf16be" => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
        "utf32le" => new UTF32Encoding(bigEndian: false, byteOrderMark: true),
        "utf32be" => new UTF32Encoding(bigEndian: true, byteOrderMark: true),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32le")]
    [InlineData("utf32be")]
    public void AWideTargetsMissingBom_IsRefused(string targetName)
    {
        byte[] original = Encoding.UTF8.GetBytes("hello world");
        string path = WriteSource("wide-missing-bom.txt", original);
        Encoding target = WideTarget(targetName);
        byte[] preamble = target.GetPreamble();
        byte[]? beforeDamage = null;

        ConversionResult result = ConvertFromUtf8(
            path, target, writeBom: true, damage: temporaryOutput =>
            {
                beforeDamage = File.ReadAllBytes(temporaryOutput);
                byte[] bytes = [.. beforeDamage];
                Array.Fill(bytes, (byte)' ', 0, preamble.Length);
                File.WriteAllBytes(temporaryOutput, bytes);
            });

        Assert.NotNull(beforeDamage);
        Assert.Equal(preamble, beforeDamage[..preamble.Length]);

        AssertRefusedWithSourceAndDirectoryIntact(result, path, original);
        Assert.Equal(ConversionErrorCode.BomMismatch, result.ErrorCode);
        Assert.Contains("missing the expected byte-order mark", result.ErrorMessage);
        Assert.False(result.BomVerificationPassed);
    }

    [Theory]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32le")]
    [InlineData("utf32be")]
    public void AWideTargetsUnrequestedBom_IsRefused(string targetName)
    {
        byte[] original = Encoding.UTF8.GetBytes("hello world");
        string path = WriteSource("wide-extra-bom.txt", original);
        Encoding target = WideTarget(targetName);
        byte[] preamble = target.GetPreamble();
        byte[]? beforeDamage = null;

        ConversionResult result = ConvertFromUtf8(
            path, target, writeBom: false, damage: temporaryOutput =>
            {
                beforeDamage = File.ReadAllBytes(temporaryOutput);
                byte[] bytes = [.. beforeDamage];
                preamble.CopyTo(bytes, 0);
                File.WriteAllBytes(temporaryOutput, bytes);
            });

        Assert.NotNull(beforeDamage);
        Assert.NotEqual(preamble, beforeDamage[..preamble.Length]);

        AssertRefusedWithSourceAndDirectoryIntact(result, path, original);
        Assert.Equal(ConversionErrorCode.BomMismatch, result.ErrorCode);
        Assert.Contains("unexpectedly contains a byte-order mark", result.ErrorMessage);
        Assert.False(result.BomVerificationPassed);
    }

    [Theory]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32le")]
    [InlineData("utf32be")]
    public void AnOutputThatMatchesAllButTheLastBomByte_IsNotMistakenForABom(string targetName)
    {
        // The UTF-16LE preamble FF FE is the start of the UTF-32LE preamble FF FE 00 00, so a
        // check that compared only a prefix would accept this damaged output as having its BOM.
        byte[] original = Encoding.UTF8.GetBytes("hello world");
        string path = WriteSource("wide-partial-bom.txt", original);
        Encoding target = WideTarget(targetName);
        byte[] preamble = target.GetPreamble();
        byte[]? beforeDamage = null;

        ConversionResult result = ConvertFromUtf8(
            path, target, writeBom: true, damage: temporaryOutput =>
            {
                beforeDamage = File.ReadAllBytes(temporaryOutput);
                byte[] bytes = [.. beforeDamage];
                bytes[preamble.Length - 1] ^= 0x01;
                File.WriteAllBytes(temporaryOutput, bytes);
            });

        Assert.NotNull(beforeDamage);
        Assert.Equal(preamble, beforeDamage[..preamble.Length]);

        AssertRefusedWithSourceAndDirectoryIntact(result, path, original);
        Assert.Equal(ConversionErrorCode.BomMismatch, result.ErrorCode);
        Assert.Contains("missing the expected byte-order mark", result.ErrorMessage);
    }

    [Fact]
    public void OutputThatCannotBeDecodedAtTheSameLength_IsRefusedAsADecodeError()
    {
        byte[] original = Encoding.UTF8.GetBytes("Hello world");
        string path = WriteSource("undecodable.txt", original);

        // 0xFF never occurs in UTF-8, so the strict re-decode must reject the output. The BOM
        // target keeps the length and lets the BOM check run first.
        ConversionResult result = ConvertFromUtf8(
            path, new UTF8Encoding(true), writeBom: true, damage: temporaryOutput =>
                File.WriteAllBytes(
                    temporaryOutput,
                    [.. Utf8Bom, .. Enumerable.Repeat((byte)0xFF, original.Length)]));

        AssertRefusedWithSourceAndDirectoryIntact(result, path, original);
        Assert.Equal(ConversionErrorCode.TargetDecodeError, result.ErrorCode);
        Assert.Contains("could not be re-decoded", result.ErrorMessage);
        Assert.False(result.VerificationPassed);
        Assert.True(result.BomVerificationPassed);
    }
}
