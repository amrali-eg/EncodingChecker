using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// The invariant the whole tool rests on: if preservation cannot be proved, the
/// source is not replaced.
///
/// Every case here drives a different failure mode and asserts the same two
/// things — the conversion is refused, and the original file is byte-for-byte
/// what it was. That pairing is the point. A refusal that still damaged the file
/// would be worse than no refusal at all, and the counting of "safe refusals"
/// means nothing unless each one is verified to have left the source alone.
///
/// This exists because the invariant was previously enforced but never
/// demonstrated. An audit across four corpora found a defect where conversion
/// reported success on files whose text had silently changed; the lesson taken
/// from it is that a safety property nothing tests is a safety property nobody
/// knows they still have.
/// </summary>
public sealed class ConversionSafetyInvariantTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_safety_").FullName;

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

    // EUC-JP bytes carrying a JIS X 0212 sequence introduced by SS3 (0x8F).
    // Code page 51932 has no mapping for it.
    private static readonly byte[] Unrepresentable =
        [0x8F, 0xB0, 0xDF, 0xB9, 0xA5, 0xA1, 0xA4, 0xC0, 0xA4, 0xB3, 0xA6, 0xA1, 0xAA];

    [Fact]
    public void InvalidByteSequence_RefusesAndLeavesTheSourceUnchanged()
    {
        AssertRefusedAndUnchanged(
            "invalid.txt",
            Unrepresentable,
            source: "euc-jp",
            target: "utf-8");
    }

    [Fact]
    public void TruncatedMultiByteSequence_RefusesAndLeavesTheSourceUnchanged()
    {
        // A file that ends mid-character. Strict decoding must reject it rather
        // than dropping or substituting the incomplete tail.
        byte[] truncated = Encoding.GetEncoding("shift_jis").GetBytes("日本語");
        AssertRefusedAndUnchanged(
            "truncated.txt",
            truncated[..^1],
            source: "shift_jis",
            target: "utf-8");
    }

    [Fact]
    public void ContentTheTargetCannotRepresent_RefusesAndLeavesTheSourceUnchanged()
    {
        // The encoder side: CJK has no representation in Windows-1252.
        AssertRefusedAndUnchanged(
            "unencodable.txt",
            Encoding.UTF8.GetBytes("世界 مرحبا Привет"),
            source: "utf-8",
            target: "windows-1252");
    }

    [Fact]
    public void PostWriteVerificationFailure_RefusesAndLeavesTheSourceUnchanged()
    {
        // An encoding whose code page cannot be rebuilt keeps its own codecs, so
        // strict reconstruction must fail before a substituting codec can write.
        string path = Path.Combine(_root, "verify.txt");
        byte[] original = Encoding.UTF8.GetBytes("Привет мир");
        File.WriteAllBytes(path, original);

        ConversionResult result = EncodingConverter.Convert(
            path, path, Encoding.UTF8, new SubstitutingEncoding(), new ConversionOptions());

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.SourceDecodeError, result.ErrorCode);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void ImpossibleBomRequest_RefusesBeforeTouchingTheFile()
    {
        // A target with no preamble cannot satisfy WriteBom. This is rejected
        // before any I/O, and the file must be untouched either way.
        string path = Path.Combine(_root, "nobom.txt");
        byte[] original = Encoding.UTF8.GetBytes("plain ascii text");
        File.WriteAllBytes(path, original);

        ConversionResult result = EncodingConverter.Convert(
            path, path, Encoding.UTF8, Encoding.GetEncoding("windows-1252"),
            new ConversionOptions { WriteBom = true });

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.BomMismatch, result.ErrorCode);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void BackupFailure_AbortsBeforeConvertingAnything()
    {
        // If the backup cannot be written, the conversion must not proceed:
        // a converted file with no recoverable original is the outcome backups
        // exist to prevent. A directory occupying the ".bak" path makes the
        // copy fail without needing permissions to be manipulated.
        string path = Path.Combine(_root, "backupfail.txt");
        byte[] original = Encoding.GetEncoding("windows-1252").GetBytes("café");
        File.WriteAllBytes(path, original);
        Directory.CreateDirectory(path + ".bak");

        var entry = new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = "windows-1252",
            SourceHasBom = false,
            TargetEncoding = "windows-1252",
            TargetHasBom = false,
            SourceEncodingWasSpecified = true,
        };

        var completed = new EntrySink();
        ScanEngine.ConvertFiles(
            [entry], "utf-8", targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false, backup: true, completed.Add, CancellationToken.None);

        ConversionReportEntry result = Assert.Single(completed);

        Assert.Equal(ConversionRowResult.Error, result.Result);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void RecoveryRecordFailure_RefusesAndLeavesTheSourceUnchanged()
    {
        string path = Path.Combine(_root, "recordfail.txt");
        byte[] original = Encoding.UTF8.GetBytes("café — 日本語");
        File.WriteAllBytes(path, original);

        ConversionResult result = EncodingConverter.Convert(
            path,
            path,
            Encoding.UTF8,
            new UnicodeEncoding(false, false),
            new ConversionOptions
            {
                RecordConversion = _ => "the source file could not be hashed",
            });

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.RecoveryRecordError, result.ErrorCode);
        Assert.False(result.ReplacementCommitted);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void RecoveryRecordIsCompletedOnlyAfterOutputInstallation()
    {
        const string text = "café — 日本語";
        string path = Path.Combine(_root, "record-complete.txt");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));

        var prepared = false;
        var completed = false;

        ConversionResult result = EncodingConverter.Convert(
            path,
            path,
            Encoding.UTF8,
            new UnicodeEncoding(false, false),
            new ConversionOptions
            {
                RecordConversion = record =>
                {
                    prepared = true;
                    Assert.NotEmpty(record.OutputSha256);
                    return null;
                },
                CompleteConversionRecord = () =>
                {
                    // Completion must run after replacement, not merely after preparation.
                    Assert.Equal(text, Encoding.Unicode.GetString(File.ReadAllBytes(path)));
                    completed = true;
                    return null;
                },
            });

        Assert.True(result.Success);
        Assert.True(prepared);
        Assert.True(completed);
    }

    [Fact]
    public void RecoveryRecordCompletionFailureReportsInstalledOutputTruthfully()
    {
        const string text = "café — 日本語";
        string path = Path.Combine(_root, "record-complete-fail.txt");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));

        ConversionResult result = EncodingConverter.Convert(
            path,
            path,
            Encoding.UTF8,
            new UnicodeEncoding(false, false),
            new ConversionOptions
            {
                RecordConversion = _ => null,
                CompleteConversionRecord = () => "simulated completion failure",
            });

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.RecoveryRecordError, result.ErrorCode);
        Assert.True(result.ReplacementCommitted);
        Assert.Equal(text, Encoding.Unicode.GetString(File.ReadAllBytes(path)));
        Assert.Contains("installed and verified", result.ErrorMessage);
    }

    [Fact]
    public void WhatIf_NeverModifiesAnything()
    {
        // The dry run must be exactly that, including for a file that would
        // convert successfully.
        string path = Path.Combine(_root, "whatif.txt");
        byte[] original = Encoding.GetEncoding("windows-1252").GetBytes("café");
        File.WriteAllBytes(path, original);

        var entry = new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = "windows-1252",
            SourceHasBom = false,
            TargetEncoding = "windows-1252",
            TargetHasBom = false,
        };

        var completed = new EntrySink();
        ScanEngine.ConvertFiles(
            [entry], "utf-8", targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: true, backup: false, completed.Add, CancellationToken.None);

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void Utf8BomSource_ConvertsToUtf8WithoutBom()
    {
        const string text = "café — 日本語\r\n";
        string path = Path.Combine(_root, "utf8-bom.txt");
        File.WriteAllBytes(path, [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(text)]);

        ConversionResult result = EncodingConverter.Convert(
            path, path, Encoding.UTF8, new UTF8Encoding(false), new ConversionOptions());

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(result.BomVerificationPassed);
        Assert.False(File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Equal(text, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void MultipleLeadingUtf8Boms_RefusesAndLeavesTheSourceUnchanged()
    {
        string path = Path.Combine(_root, "double-bom.txt");
        byte[] original =
        [
            .. Encoding.UTF8.Preamble,
            .. Encoding.UTF8.Preamble,
            .. Encoding.UTF8.GetBytes("text\r\n"),
        ];
        File.WriteAllBytes(path, original);

        ConversionResult result = EncodingConverter.Convert(
            path, path, Encoding.UTF8, new UTF8Encoding(false), new ConversionOptions());

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.MultipleLeadingByteOrderMarks, result.ErrorCode);
        Assert.Contains("multiple", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void ASuccessfulConversionStillProvesPreservation()
    {
        // The invariant must not be satisfied by refusing everything. A file
        // that can be proved preserved has to convert, and the result has to be
        // the same text.
        const string text = "café — naïve — 日本語\r\n";
        string path = Path.Combine(_root, "good.txt");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));

        ConversionResult result = EncodingConverter.Convert(
            path, path, Encoding.UTF8, new UTF8Encoding(false), new ConversionOptions());

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(result.VerificationPassed);
        Assert.Equal(text, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void SourceThatNoLongerMatchesWhatWasApproved_RefusesAtInstallation()
    {
        // The preflight in -Apply proves every file matched when the run started. A
        // large tree can take a while after that, and the length-and-timestamp recheck
        // inside the converter only compares against what this run itself saw on
        // opening the file - it cannot speak to a decision made before that.
        string path = Path.Combine(_root, "approved.txt");
        byte[] original = Encoding.GetEncoding("shift_jis").GetBytes("こんにちは世界");
        File.WriteAllBytes(path, original);

        ConversionResult result = EncodingConverter.Convert(
            path,
            path,
            Encoding.GetEncoding("shift_jis"),
            Encoding.UTF8,
            new ConversionOptions
            {
                // The hash of something else entirely: what an earlier plan would have
                // recorded for a file that has since been rewritten.
                ExpectedSourceSha256 = new string('0', 64),
            },
            progress: null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.SourceChangedDuringConversion, result.ErrorCode);
        Assert.False(result.ReplacementCommitted);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void SourceThatStillMatchesWhatWasApproved_Converts()
    {
        // The other direction, which matters just as much: a check that refuses
        // everything is not a safety feature, it is a broken one.
        const string text = "こんにちは世界";
        string path = Path.Combine(_root, "unchanged.txt");
        File.WriteAllBytes(path, Encoding.GetEncoding("shift_jis").GetBytes(text));

        ConversionResult result = EncodingConverter.Convert(
            path,
            path,
            Encoding.GetEncoding("shift_jis"),
            Encoding.UTF8,
            new ConversionOptions
            {
                ExpectedSourceSha256 = ConversionMetadataStore.ComputeSha256(path),
            },
            progress: null,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(text, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void AnOrdinaryConversionDoesNotPayForTheApprovalCheck()
    {
        // The stronger check costs a second full read of every file. Conversions that
        // nothing committed to in advance have nothing to compare against, so they keep
        // the length-and-timestamp recheck and skip the extra pass.
        const string text = "こんにちは世界";
        string path = Path.Combine(_root, "ordinary.txt");
        File.WriteAllBytes(path, Encoding.GetEncoding("shift_jis").GetBytes(text));

        Assert.Null(ConversionOptions.Default.ExpectedSourceSha256);

        ConversionResult result = EncodingConverter.Convert(
            path, path,
            Encoding.GetEncoding("shift_jis"), Encoding.UTF8,
            ConversionOptions.Default, progress: null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(text, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    private void AssertRefusedAndUnchanged(
        string name, byte[] content, string source, string target)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, content);

        var entry = new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = source,
            SourceHasBom = false,
            TargetEncoding = source,
            TargetHasBom = false,
            SourceEncodingWasSpecified = true,
        };

        var completed = new EntrySink();
        ScanEngine.ConvertFiles(
            [entry], target, targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false, backup: false, completed.Add, CancellationToken.None);

        ConversionReportEntry result = Assert.Single(completed);

        Assert.Equal(ConversionRowResult.Error, result.Result);
        Assert.Equal(content, File.ReadAllBytes(path));
    }

    /// <summary>
    /// Substitutes rather than throwing, and reports a code page that cannot be
    /// rebuilt, so strict reconstruction cannot rescue it.
    /// </summary>
    private sealed class SubstitutingEncoding : Encoding
    {
        public override int CodePage => 65_000_003;

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
}
