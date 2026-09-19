using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// A change that happens while a conversion is running must be refused, not installed over.
/// </summary>
/// <remarks>
/// The final progress report is delivered after the last byte is written and before the
/// source is rechecked, so a report handler that touches the source lands in exactly the
/// window the recheck exists to close. The handler runs inline (not through
/// <see cref="Progress{T}"/>, which posts to a synchronization context) so the change is
/// made before the recheck rather than at some later point.
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
        CancellationToken cancellationToken = default) =>
        EncodingConverter.Convert(
            path,
            path,
            new UTF8Encoding(false),
            new UTF8Encoding(false),
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

        ConversionResult result = Convert(
            path,
            new InlineProgress(_ =>
            {
                reports++;
                throw new InvalidOperationException("the UI went away");
            }));

        Assert.True(reports > 0, "the handler was never invoked, so nothing was tested");
        Assert.True(result.Success);
        Assert.Equal(original, File.ReadAllBytes(path));
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
