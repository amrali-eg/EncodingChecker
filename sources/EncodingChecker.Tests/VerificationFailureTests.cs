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

    private static ConversionResult ConvertUtf8ToUtf8(
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

        ConversionResult result = ConvertUtf8ToUtf8(
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
    }

    [Fact]
    public void ABomThatWasNotRequestedButAppearsInTheOutput_IsRefused()
    {
        byte[] original = Encoding.UTF8.GetBytes("hello world");
        string path = WriteSource("extra-bom.txt", original);
        byte[]? beforeDamage = null;

        // Encoding.UTF8 has a preamble, so the BOM check runs; WriteBom = false asks for none.
        ConversionResult result = ConvertUtf8ToUtf8(
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
    }

    [Fact]
    public void OutputWhoseTextDiffersAtTheSameLength_IsRefusedAsAContentMismatch()
    {
        byte[] original = Encoding.UTF8.GetBytes("Hello world");
        string path = WriteSource("same-length.txt", original);

        // A BOM target, so the BOM check runs and passes and BomVerificationPassed says so.
        ConversionResult result = ConvertUtf8ToUtf8(
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

        ConversionResult result = ConvertUtf8ToUtf8(
            path, new UTF8Encoding(true), writeBom: true, damage: temporaryOutput =>
                File.WriteAllBytes(temporaryOutput, [.. Utf8Bom, 0xF0, 0x9F, 0x98, 0x80]));

        AssertRefusedWithSourceAndDirectoryIntact(result, path, original);
        Assert.Equal(ConversionErrorCode.UnicodeMismatch, result.ErrorCode);
        Assert.Contains("Decoded content length differs from source", result.ErrorMessage);
        Assert.Equal(1, result.UnicodeScalarsVerified);
        Assert.False(result.VerificationPassed);
        Assert.True(result.BomVerificationPassed);
    }
}
