using System.Text;
using System.Threading;

namespace EncodingChecker.Tests;

/// <summary>
/// A decode failure has to describe something the reader can find.
/// </summary>
/// <remarks>
/// The diagnostic reported <c>DecoderFallbackException.Index</c> as "offset N within the
/// failing read chunk". That index is relative to the decoder call, not the file, and is
/// negative when the bad sequence began in bytes carried over from the previous call, so a
/// truncated tail produced "offset -2" — a position no file has, in a message naming a
/// frame it did not describe. The offending bytes are reported instead.
/// </remarks>
public sealed class DecodeFailureDiagnosticTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec-decode-").FullName;

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

    /// <summary>
    /// Converts one file, naming the source codec so the decoder is what fails.
    /// </summary>
    /// <remarks>
    /// Malformed UTF-8 is valid windows-1252, so automatic detection relabels these files
    /// as legacy text and the policy refuses them before any decoding happens. Naming the
    /// source is how a caller reaches the decoder with bytes it cannot accept.
    /// </remarks>
    private ConversionReportEntry Convert(byte[] contents, string? from = null)
    {
        File.WriteAllBytes(Path.Combine(_root, "f.txt"), contents);

        var entries = new EntrySink();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                Action = ScanAction.Convert,
                TargetCharset = "utf-16",
                SourceCharset = from,
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
    public void ATruncatedTrailingSequenceNamesTheBytes()
    {
        // Begins in one decoder call and fails in the flush: the case that produced the
        // negative index, and the one automatic detection still calls UTF-8.
        ConversionReportEntry entry = Convert([.. Cjk(3), 0xE4, 0xB8]);

        Assert.Equal(ConversionRowResult.Error, entry.Result);
        Assert.Equal(nameof(ConversionErrorCode.SourceDecodeError), entry.ReasonCode);
        Assert.Contains("0xE4B8", entry.Diagnostic);
    }

    [Theory]
    [InlineData(new byte[] { 0xC0, 0xAF }, "0xC0")]   // overlong "/"
    [InlineData(new byte[] { 0x80 }, "0x80")]         // lone continuation byte
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 }, "0xED")] // CESU-8 surrogate
    public void AnInvalidSequenceMidFileNamesTheBytes(byte[] bad, string expected)
    {
        ConversionReportEntry entry =
            Convert([.. Cjk(3), .. bad, .. Cjk(3)], from: "utf-8");

        Assert.Equal(ConversionRowResult.Error, entry.Result);
        Assert.Equal(nameof(ConversionErrorCode.SourceDecodeError), entry.ReasonCode);
        Assert.Contains(expected, entry.Diagnostic);
    }

    [Fact]
    public void NoDecodeDiagnosticDescribesAPositionItCannotKnow()
    {
        // The wording is the regression: an index relative to a decoder call was being
        // presented as an offset within a frame the reader cannot locate.
        foreach ((byte[] contents, string? from) in new (byte[], string?)[]
        {
            ([.. Cjk(3), 0xE4, 0xB8], null),
            ([.. Cjk(3), 0xC0, 0xAF, .. Cjk(3)], "utf-8"),
            ([.. Cjk(3), 0x80, .. Cjk(3)], "utf-8"),
        })
        {
            string diagnostic = Convert(contents, from).Diagnostic ?? string.Empty;

            Assert.Contains("invalid byte sequence", diagnostic);
            Assert.DoesNotContain("offset", diagnostic, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("chunk", diagnostic, StringComparison.OrdinalIgnoreCase);
        }
    }
}
