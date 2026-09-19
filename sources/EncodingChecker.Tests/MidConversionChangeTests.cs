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
