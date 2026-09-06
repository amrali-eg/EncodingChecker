using System.Text;
using System.Threading;

namespace EncodingChecker.Tests;

/// <summary>
/// A preview must not promise a conversion that would fail.
/// </summary>
/// <remarks>
/// <c>ApplyConversion</c> returned at the <c>whatIf</c> branch before the converter ran, so
/// nothing decoded the file. <c>-Plan</c> sets <c>WhatIf</c>, so a plan recorded
/// <c>Action = Convert</c> with no reason for a source that cannot be read, exited 0, and
/// showed the reviewer nothing; the failure surfaced at <c>-Apply</c>, after approval and
/// part-way through the batch. <c>FindStaleFiles</c> can prove the bytes have not changed
/// since review and cannot prove they are readable, because that needs a decode nobody
/// performed.
/// <para>
/// The decode only. A source that reads cleanly can still fail on a target that cannot
/// represent it — <c>TargetEncodeError</c> — and previewing that means running the whole
/// conversion into a discarded buffer. <see cref="ATargetThatCannotRepresentTheTextIsStillNotPredicted"/>
/// records that limit rather than leaving it to be discovered.
/// </para>
/// </remarks>
public sealed class PreviewVerificationTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec-preview-").FullName;

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

    /// <summary>Clean text past the 64 KiB detection sample, optionally spoiled at the end.</summary>
    private static byte[] PastTheSample(bool corrupt)
    {
        var text = new StringBuilder();

        while (Encoding.UTF8.GetByteCount(text.ToString()) < 70 * 1024)
            text.Append("the quick brown fox jumps over the lazy dog. ");

        byte[] clean = new UTF8Encoding(false).GetBytes(text.ToString());

        return corrupt ? [.. clean, 0xC0, 0xAF] : clean;
    }

    private ConversionReportEntry Preview(byte[] contents, string target, string? from = null)
    {
        File.WriteAllBytes(Path.Combine(_root, "f.txt"), contents);

        var entries = new EntrySink();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                Action = ScanAction.Convert,
                TargetCharset = target,
                SourceCharset = from,
                WhatIf = true,
                MaxParallelism = 1,
            },
            entries.Add,
            CancellationToken.None);

        return entries.Single();
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("us-ascii")]
    public void APreviewRefusesWhatARealRunWouldRefuse(string target)
    {
        ConversionReportEntry entry = Preview(PastTheSample(corrupt: true), target);

        Assert.Equal(ConversionRowResult.Error, entry.Result);
        Assert.Equal(ConversionReasonCodes.StrictValidationFailed, entry.ReasonCode);
        Assert.False(string.IsNullOrEmpty(entry.Diagnostic));
    }

    [Fact]
    public void APlanDoesNotScheduleAFileItCannotCarryOut()
    {
        // The action is what the plan records, so a preview that merely reported an error
        // while still saying Convert would put it in front of a reviewer as approved work.
        ConversionReportEntry entry = Preview(PastTheSample(corrupt: true), "utf-16");

        Assert.Equal(PlannedAction.Refuse, entry.Action);
        Assert.False(entry.ReplacementCommitted);

        ConversionPlan plan = ConversionPlan.FromEntries(
            [entry], _root, "utf-16", targetHasBom: false,
            backupEnabled: false, explicitSource: null);

        PlannedFile planned = Assert.Single(plan.Files);

        Assert.Equal(PlannedAction.Refuse, planned.Action);
        Assert.False(planned.NeedsSourceChoice);
        Assert.Equal(0, plan.Summary.ReadyToConvert);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    public void AnOrdinaryFileStillPreviewsAsConvertible(string target)
    {
        // The check must not start refusing the files a preview exists to describe, and
        // 70 KiB puts the clean tail well past the detection sample.
        ConversionReportEntry entry = Preview(PastTheSample(corrupt: false), target);

        Assert.Equal(PlannedAction.Convert, entry.Action);
        Assert.Equal(ConversionRowResult.Converted, entry.Result);
        Assert.Null(entry.ReasonCode);
    }

    [Fact]
    public void APreviewStillWritesNothing()
    {
        byte[] contents = PastTheSample(corrupt: false);

        Preview(contents, "utf-16");

        Assert.Equal(contents, File.ReadAllBytes(Path.Combine(_root, "f.txt")));
        Assert.Empty(Directory.GetFiles(_root, "*.bak"));
        Assert.Empty(Directory.GetFiles(_root, "*" + ConversionMetadataStore.Suffix));
    }

    [Fact]
    public void ATargetThatCannotRepresentTheTextIsStillNotPredicted()
    {
        // The deliberate limit of this fix, pinned so it is a known gap rather than a
        // surprise: the source decodes cleanly, so the preview says it would convert, and
        // the real run fails encoding it into a target that has no Cyrillic.
        byte[] cyrillic = new UTF8Encoding(false).GetBytes("Здравствуй, мир!\n");

        Assert.Equal(ConversionRowResult.Converted, Preview(cyrillic, "us-ascii").Result);

        var real = new EntrySink();
        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                Action = ScanAction.Convert,
                TargetCharset = "us-ascii",
                MaxParallelism = 1,
            },
            real.Add,
            CancellationToken.None);

        Assert.Equal(ConversionRowResult.Error, real.Single().Result);
        Assert.Equal(
            nameof(ConversionErrorCode.TargetEncodeError), real.Single().ReasonCode);
    }
}
