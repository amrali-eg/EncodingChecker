using System.Text;
using System.Threading;

namespace EncodingChecker.Tests;

/// <summary>
/// "Already in the target encoding" has to be true of the whole file.
/// </summary>
/// <remarks>
/// Detection reads at most 64 KiB, and when the source codec matched the target nothing read
/// further. A file whose first 64 KiB was clean and whose later bytes were not valid in the
/// codec EC had just named was reported <c>Unchanged</c> — and whether EC noticed depended
/// only on which target the caller typed: the same corrupt file came back <c>Error</c> under
/// <c>-Target utf-16</c> and <c>Unchanged</c> under <c>-Target utf-8</c>. <c>-Validate</c>
/// always read the whole file; the Convert path simply never reached that check once it had
/// decided it had nothing to do.
/// </remarks>
public sealed class UnchangedVerificationTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec-unchanged-").FullName;

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

    /// <summary>Clean text well past the detection sample, optionally spoiled at the end.</summary>
    private static byte[] PastTheSample(bool ascii, bool corrupt)
    {
        string unit = ascii ? "the quick brown fox jumps over the lazy dog. " : "中文内容测试 ";
        var text = new StringBuilder();

        while (Encoding.UTF8.GetByteCount(text.ToString()) < 70 * 1024)
            text.Append(unit);

        byte[] clean = new UTF8Encoding(false).GetBytes(text.ToString());

        // An overlong "/" — invalid in UTF-8 and outside us-ascii alike.
        return corrupt ? [.. clean, 0xC0, 0xAF] : clean;
    }

    private ConversionReportEntry Convert(byte[] contents, string target)
    {
        File.WriteAllBytes(Path.Combine(_root, "f.txt"), contents);

        var entries = new EntrySink();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                Action = ScanAction.Convert,
                TargetCharset = target,
                MaxParallelism = 1,
            },
            entries.Add,
            CancellationToken.None);

        return entries.Single();
    }

    [Theory]
    [InlineData(true, "us-ascii")]
    [InlineData(true, "utf-8")]
    [InlineData(true, "utf-16")]
    [InlineData(false, "us-ascii")]
    [InlineData(false, "utf-8")]
    [InlineData(false, "utf-16")]
    public void TheSameCorruptFileFailsUnderEveryTarget(bool ascii, string target)
    {
        // The matrix is the finding: the outcome must not depend on which target is named.
        byte[] corrupt = PastTheSample(ascii, corrupt: true);

        ConversionReportEntry entry = Convert(corrupt, target);

        Assert.Equal(ConversionRowResult.Error, entry.Result);
        Assert.False(string.IsNullOrEmpty(entry.ReasonCode));
        Assert.Equal(corrupt, File.ReadAllBytes(Path.Combine(_root, "f.txt")));
    }

    [Fact]
    public void TheUnchangedPathNamesTheCheckThatFailed()
    {
        // Reported as the pre-check it is, not as a conversion that went wrong: no write
        // was attempted at all.
        ConversionReportEntry entry = Convert(PastTheSample(ascii: true, corrupt: true), "us-ascii");

        Assert.Equal(PlannedAction.Unchanged, entry.Action);
        Assert.Equal(ConversionRowResult.Error, entry.Result);
        Assert.Equal(ConversionReasonCodes.StrictValidationFailed, entry.ReasonCode);
        Assert.False(entry.ReplacementCommitted);
        Assert.False(string.IsNullOrEmpty(entry.Diagnostic));
    }

    [Theory]
    [InlineData(true, "us-ascii")]
    [InlineData(false, "utf-8")]
    public void AFileThatReallyIsInTheTargetEncodingIsStillUnchanged(bool ascii, string target)
    {
        // The check must not start refusing the ordinary case it was added to describe
        // honestly — including well past the 64 KiB sample.
        byte[] clean = PastTheSample(ascii, corrupt: false);

        ConversionReportEntry entry = Convert(clean, target);

        Assert.Equal(PlannedAction.Unchanged, entry.Action);
        Assert.Equal(ConversionRowResult.Unchanged, entry.Result);
        Assert.Null(entry.ReasonCode);
        Assert.Equal(clean, File.ReadAllBytes(Path.Combine(_root, "f.txt")));
    }

    [Fact]
    public void AShortCleanFileIsStillUnchanged()
    {
        ConversionReportEntry entry = Convert(
            new UTF8Encoding(false).GetBytes("plain ascii content\n"), "us-ascii");

        Assert.Equal(ConversionRowResult.Unchanged, entry.Result);
        Assert.Null(entry.ReasonCode);
    }

    [Fact]
    public void ConvertAndValidateNowAgreeAboutTheSameFile()
    {
        // -Validate always caught this. The two modes disagreeing about one file was the
        // shape of the defect.
        byte[] corrupt = PastTheSample(ascii: true, corrupt: true);
        File.WriteAllBytes(Path.Combine(_root, "f.txt"), corrupt);

        var validated = new EntrySink();
        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                Action = ScanAction.Validate,
                ValidCharsets = ["us-ascii", "utf-8"],
                MaxParallelism = 1,
            },
            validated.Add,
            CancellationToken.None);

        Assert.Equal(ConversionRowResult.Invalid, validated.Single().Result);
        Assert.Equal(
            ConversionReasonCodes.StrictValidationFailed, validated.Single().ReasonCode);

        // Same file, same reason, from the Convert path.
        Assert.Equal(
            ConversionReasonCodes.StrictValidationFailed,
            Convert(corrupt, "us-ascii").ReasonCode);
    }
}
