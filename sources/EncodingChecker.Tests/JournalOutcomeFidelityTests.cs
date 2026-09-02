using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// The journal must describe what became of each file, not what was intended for it.
/// </summary>
/// <remarks>
/// One status table was wrong in both directions. A file EC never opened was recorded
/// as <c>Refused</c> - a safety decision - while the run reporting it exited 3, so the
/// audit record and the exit code disagreed about every locked file. And conversion can
/// fail after the replacement is installed, when the recovery record cannot be marked
/// complete or attributes cannot be restored; those were recorded as <c>Failed</c>,
/// documented as "not touched", for a file that had already been rewritten.
/// <para>
/// The post-installation cases are asserted at the journal boundary rather than end to
/// end. Both need an I/O failure inside a window of a few instructions after
/// <c>File.Replace</c> returns, which cannot be produced reliably from outside the
/// process; what is testable, and what was wrong, is the mapping.
/// </para>
/// </remarks>
public sealed class JournalOutcomeFidelityTests : IDisposable
{
    private const int ExpectedProcessingErrors = 3;

    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_journalfidelity_").FullName;

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

    private static int Run(params string[] args)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;

        try
        {
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());

            return Program.RunConsoleMode(args);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private string Write(string name, string content = "plain ascii")
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));

        return path;
    }

    private JournalEntry Journal(ConversionReportEntry entry) =>
        Assert.Single(
            ConversionJournal.FromRun(
                [entry], _root, "utf-8", targetHasBom: false, backupEnabled: true,
                explicitSource: null, surface: "Test",
                startedUtc: DateTime.UtcNow).Entries);

    [Fact]
    public void AFileThatCouldNotBeOpenedIsFailedNotRefused()
    {
        // End to end, because this one is producible: an exclusive handle denies the
        // scan the same way another process would.
        Write("good.txt");
        string locked = Write("locked.txt");
        string journalPath = Path.Combine(_root, "journal.json");

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(
                ExpectedProcessingErrors,
                Run("-BasePath", _root, "-Target", "utf-8-bom",
                    "-Journal", journalPath, "-Quiet"));
        }

        ConversionJournal written = ReadJournal(journalPath);

        JournalEntry failed =
            Assert.Single(written.Entries, e => e.RelativePath == "locked.txt");

        // The run exits 3. An audit record calling that a policy refusal contradicts it.
        Assert.Equal(ConversionStatus.Failed, failed.Status);
        Assert.Equal(ConversionReasonCodes.ScanFailed, failed.ReasonCode);
        Assert.Null(failed.Sha256After);
    }

    [Fact]
    public void AGenuineRefusalIsStillRefused()
    {
        // The control. Mapping every error to Failed would satisfy the test above while
        // erasing the distinction the journal exists to record.
        File.WriteAllBytes(
            Path.Combine(_root, "ambiguous.txt"),
            new UnicodeEncoding(bigEndian: false, byteOrderMark: false)
                .GetBytes("Hello World, this is plain text."));
        string journalPath = Path.Combine(_root, "journal.json");

        Run("-BasePath", _root, "-Target", "utf-8", "-Journal", journalPath, "-Quiet");

        JournalEntry refused = Assert.Single(ReadJournal(journalPath).Entries);

        Assert.Equal(ConversionStatus.Refused, refused.Status);
        Assert.Equal(ConversionReasonCodes.AmbiguousBomlessUtf16, refused.ReasonCode);
    }

    [Fact]
    public void AFailureAfterInstallationIsNotReportedAsUntouched()
    {
        // ReplacementCommitted: true with a failure is exactly what RecoveryRecordError
        // and MetadataRestoreFailed produce once the file is already replaced.
        string path = Write("installed.txt");

        JournalEntry recorded = Journal(new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = "utf-8",
            TargetEncoding = "utf-8",
            Action = PlannedAction.Convert,
            SourceInterpretation = SourceInterpretation.AutomaticUnicodeOrAscii,
            Result = ConversionRowResult.Error,
            ReplacementCommitted = true,
            OutputSha256 = new string('a', 64),
            ReasonCode = nameof(ConversionErrorCode.RecoveryRecordError),
        });

        Assert.Equal(ConversionStatus.ConvertedWithWarning, recorded.Status);

        // The file changed, so it needs an after-hash; Failed would have left it null.
        Assert.Equal(new string('a', 64), recorded.Sha256After);
    }

    [Fact]
    public void AnUndeterminedInstallationSaysSoRatherThanGuessing()
    {
        // ReplacementCommitted: null is the converter reporting that it cannot tell.
        // Failed would claim the file is untouched and ConvertedWithWarning would claim
        // it was installed; neither was established.
        string path = Write("unknown.txt", "what is actually on disk");

        JournalEntry recorded = Journal(new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = "utf-8",
            TargetEncoding = "utf-8",
            Action = PlannedAction.Convert,
            SourceInterpretation = SourceInterpretation.AutomaticUnicodeOrAscii,
            Result = ConversionRowResult.Error,
            ReplacementCommitted = null,
            OutputSha256 = new string('a', 64),
            ReasonCode = nameof(ConversionErrorCode.ReplacementError),
        });

        Assert.Equal(ConversionStatus.InstallationUnknown, recorded.Status);

        // Whether those verified bytes were installed is the open question, so the
        // record reports what is on disk instead of asserting the answer.
        Assert.NotEqual(new string('a', 64), recorded.Sha256After);
        Assert.Equal(ConversionMetadataStore.ComputeSha256(path), recorded.Sha256After);
    }

    [Fact]
    public void AFailureBeforeInstallationIsStillFailedAndUntouched()
    {
        // The control for both cases above. Backup and verification failures leave the
        // original in place, and that is exactly what Failed is for.
        string path = Write("untouched.txt");

        JournalEntry recorded = Journal(new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = "utf-8",
            TargetEncoding = "utf-8",
            Action = PlannedAction.Convert,
            SourceInterpretation = SourceInterpretation.AutomaticUnicodeOrAscii,
            Result = ConversionRowResult.Error,
            ReplacementCommitted = false,
            ReasonCode = ConversionReasonCodes.BackupFailed,
        });

        Assert.Equal(ConversionStatus.Failed, recorded.Status);
        Assert.Null(recorded.Sha256After);
    }

    [Fact]
    public void TheAfterHashIsTheOneVerificationPassed()
    {
        // The journal is written after the whole batch, so re-reading records whatever
        // is on disk by then rather than what this run installed and verified. The two
        // are made to differ here so the assertion can tell which was used.
        string path = Write("verified.txt", "the bytes now on disk");
        string verified = new('b', 64);

        JournalEntry recorded = Journal(new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = "utf-8",
            TargetEncoding = "utf-8",
            Action = PlannedAction.Convert,
            SourceInterpretation = SourceInterpretation.AutomaticUnicodeOrAscii,
            Result = ConversionRowResult.Converted,
            ReplacementCommitted = true,
            OutputSha256 = verified,
        });

        Assert.Equal(ConversionStatus.Converted, recorded.Status);
        Assert.Equal(verified, recorded.Sha256After);
        Assert.NotEqual(ConversionMetadataStore.ComputeSha256(path), recorded.Sha256After);
    }

    [Fact]
    public void WithoutARecoveryRecordTheAfterHashFallsBackToReadingTheFile()
    {
        // No backup means no verified output hash was ever computed, so the fallback
        // has to stay. Losing it would leave converted files with no after-hash at all.
        string path = Write("nobackup.txt");

        JournalEntry recorded = Journal(new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = "utf-8",
            TargetEncoding = "utf-8",
            Action = PlannedAction.Convert,
            SourceInterpretation = SourceInterpretation.AutomaticUnicodeOrAscii,
            Result = ConversionRowResult.Converted,
            ReplacementCommitted = true,
            OutputSha256 = null,
        });

        Assert.Equal(ConversionMetadataStore.ComputeSha256(path), recorded.Sha256After);
    }

    private static ConversionJournal ReadJournal(string path)
    {
        using FileStream stream = File.OpenRead(path);

        return System.Text.Json.JsonSerializer.Deserialize<ConversionJournal>(stream)!;
    }
}
