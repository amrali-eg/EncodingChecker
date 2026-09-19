using System.Security.Cryptography;
using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// Conversion stays safe when the source changes, or the progress callback misbehaves, while it
/// runs.
/// </summary>
/// <remarks>
/// The final progress report is delivered after the last byte is written and before the
/// source is rechecked, so a report handler that touches the source lands in exactly the
/// window the recheck exists to close. The handler runs inline because
/// <see cref="Progress{T}"/> invokes its handler asynchronously, possibly after the recheck,
/// and the change would then be missed. The other tests use the same seam to make the callback
/// throw or cancel.
/// </remarks>
public sealed class MidConversionChangeTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_midrun_").FullName;

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

    private sealed class InlineProgress(Action<ConversionProgress> onReport)
        : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => onReport(value);
    }

    private string WriteSource(byte[] bytes)
    {
        string path = Path.Combine(_root, "source.txt");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static ConversionResult Convert(
        string path,
        IProgress<ConversionProgress>? progress,
        Encoding? target = null,
        CancellationToken cancellationToken = default) =>
        EncodingConverter.Convert(
            path,
            path,
            new UTF8Encoding(false),
            target ?? new UTF8Encoding(false),
            new ConversionOptions(),
            progress,
            cancellationToken);

    [Fact]
    public void ASourceTouchedAfterItWasReadButBeforeInstallation_IsRefused()
    {
        byte[] original = Encoding.UTF8.GetBytes("Привет мир");
        string path = WriteSource(original);
        DateTime touched = File.GetLastWriteTimeUtc(path).AddHours(1);

        ConversionResult result = Convert(
            path,
            new InlineProgress(_ => File.SetLastWriteTimeUtc(path, touched)));

        // A progress handler's exceptions are swallowed, so confirm the touch itself happened;
        // otherwise a failed touch would surface below as an unrelated success.
        Assert.Equal(touched, File.GetLastWriteTimeUtc(path));

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.SourceChangedDuringConversion, result.ErrorCode);
        Assert.False(result.ReplacementCommitted);
        Assert.Equal(original, File.ReadAllBytes(path));

        // The temporary output must not be left beside the source.
        Assert.Equal([path], Directory.GetFiles(_root));
    }

    [Fact]
    public void AProgressHandlerThatThrows_DoesNotFailTheConversion()
    {
        byte[] original = Encoding.UTF8.GetBytes("plain text");
        string path = WriteSource(original);
        int reports = 0;

        // UTF-8 to UTF-16 so the installed bytes differ from the source: an unchanged file would
        // mean nothing was installed.
        Encoding utf16 = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);

        ConversionResult result = Convert(
            path,
            new InlineProgress(_ =>
            {
                reports++;
                throw new InvalidOperationException("the UI went away");
            }),
            utf16);

        Assert.True(reports > 0, "the handler was never invoked, so nothing was tested");
        Assert.True(result.Success);
        Assert.True(result.ReplacementCommitted);
        Assert.Equal(utf16.GetBytes("plain text"), File.ReadAllBytes(path));
        Assert.Equal([path], Directory.GetFiles(_root));
    }

    [Fact]
    public void CancellationRaisedFromAProgressHandler_StopsTheConversionAndKeepsTheSource()
    {
        byte[] original = Encoding.UTF8.GetBytes("plain text");
        string path = WriteSource(original);

        ConversionResult result = Convert(
            path,
            new InlineProgress(_ => throw new OperationCanceledException()));

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.Cancelled, result.ErrorCode);
        Assert.False(result.ReplacementCommitted);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal([path], Directory.GetFiles(_root));
    }

    [Fact]
    public void ACancelledTokenRaisedDuringTheFinalReport_StopsTheConversionAndKeepsTheSource()
    {
        // Distinct from the handler throwing: here the handler only requests cancellation and
        // the converter has to notice the token itself before it installs. Several checkpoints
        // (before the recheck, before installation, and in the verification read) each honor
        // the token, so this pins that at least one of them does, not which one.
        byte[] original = Encoding.UTF8.GetBytes("plain text");
        string path = WriteSource(original);
        using var cancellation = new CancellationTokenSource();

        ConversionResult result = Convert(
            path,
            new InlineProgress(_ => cancellation.Cancel()),
            cancellationToken: cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.Cancelled, result.ErrorCode);
        Assert.False(result.ReplacementCommitted);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal([path], Directory.GetFiles(_root));
    }

    // The recheck before verification compares timestamp and length. The check after it compares
    // a hash of the source, which is what catches a change that leaves both of those alone.
    private static ConversionResult ConvertExpectingHash(
        string path, string expectedSha256, Action beforeVerify) =>
        EncodingConverter.Convert(
            path,
            path,
            new UTF8Encoding(false),
            new UTF8Encoding(false),
            new ConversionOptions
            {
                ExpectedSourceSha256 = expectedSha256,
                BeforeVerifyTemporaryOutput = _ => beforeVerify(),
            });

    private static string Sha256Of(byte[] bytes) =>
        System.Convert.ToHexStringLower(SHA256.HashData(bytes));

    [Fact]
    public void ASourceThatStillMatchesItsApprovedHash_IsConverted()
    {
        // The control for the tests below: same options, nothing disturbed.
        byte[] original = Encoding.UTF8.GetBytes("Привет мир");
        string path = WriteSource(original);

        ConversionResult result = ConvertExpectingHash(path, Sha256Of(original), () => { });

        Assert.True(result.Success);
        Assert.True(result.ReplacementCommitted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASourceRewrittenWithItsTimestampRestored_IsRefusedByTheHashCheck(bool changeLength)
    {
        byte[] original = Encoding.UTF8.GetBytes("Привет мир");
        string path = WriteSource(original);
        DateTime written = File.GetLastWriteTimeUtc(path);

        // Same byte length by default (т and д are both two bytes in UTF-8), or one byte longer.
        byte[] changed = changeLength
            ? [.. original, (byte)'!']
            : Encoding.UTF8.GetBytes("Привед мир");
        Assert.Equal(changeLength, changed.Length != original.Length);

        ConversionResult result = ConvertExpectingHash(
            path,
            Sha256Of(original),
            () =>
            {
                File.WriteAllBytes(path, changed);
                File.SetLastWriteTimeUtc(path, written);
            });

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.SourceChangedDuringConversion, result.ErrorCode);
        Assert.Contains("no longer matches", result.ErrorMessage);
        Assert.False(result.ReplacementCommitted);

        // The file still holds the rewrite: the converted original was not installed over it.
        Assert.Equal(changed, File.ReadAllBytes(path));
        Assert.Equal([path], Directory.GetFiles(_root));
    }

    [Fact]
    public void ASourceThatCannotBeReReadForTheHashCheck_IsRefusedWithThatReason()
    {
        byte[] original = Encoding.UTF8.GetBytes("Привет мир");
        string path = WriteSource(original);
        FileStream? held = null;
        ConversionResult result;

        try
        {
            result = ConvertExpectingHash(
                path,
                Sha256Of(original),
                () => held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None));
        }
        finally
        {
            held?.Dispose();
        }

        Assert.NotNull(held);
        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.SourceChangedDuringConversion, result.ErrorCode);
        Assert.Contains("could not be re-read", result.ErrorMessage);
        Assert.False(result.ReplacementCommitted);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal([path], Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveBufferSize_IsRejectedBeforeTheSourceIsTouched(int bufferSize)
    {
        byte[] original = Encoding.UTF8.GetBytes("plain text");
        string path = WriteSource(original);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EncodingConverter.Convert(
                path,
                path,
                new UTF8Encoding(false),
                new UTF8Encoding(false),
                new ConversionOptions { BufferSize = bufferSize }));

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal([path], Directory.GetFiles(_root));
    }
}
