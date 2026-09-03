using System.Text;

using System.Text.Json;

namespace EncodingChecker.Tests;

/// <summary>
/// The record of what a conversion actually did.
///
/// The chain this completes is: what EC believed, what it decided, what was approved, and
/// what it wrote. Each link has been shown to matter. The audit found conversions that
/// reported success while the text had changed — believing and writing had come apart with
/// nothing recording it. The GUI defect found conversions EC had never decided on at all.
/// And "why was this file not converted?" is asked far more often than "how do I put this
/// one back?", yet only the second had an answer, in a sidecar written solely where a
/// backup existed.
///
/// So the journal covers the run whole: refused and skipped files included, the encoding
/// the conversion actually read each file as rather than the detector's raw output, and
/// the file's SHA-256 before and after.
/// </summary>
public sealed class ConversionJournalTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_journal_").FullName;

    private string JournalPath => Path.Combine(_root, "journal.json");

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

    private string Write(string name, string text, string charset)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, Encoding.GetEncoding(charset).GetBytes(text));
        return path;
    }

    private static int Cli(params string[] args)
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

    private ConversionJournal Load()
    {
        ConversionJournal? journal = JsonSerializer.Deserialize<ConversionJournal>(
            File.ReadAllText(JournalPath));
        Assert.NotNull(journal);

        return journal!;
    }

    private JournalEntry EntryFor(string name) =>
        Assert.Single(
            Load().Entries,
            e => string.Equals(e.RelativePath, name, StringComparison.Ordinal));

    private int Convert(params string[] extra) =>
        Cli([
            "-BasePath", _root, "-Target", "utf-8",
            "-Journal", JournalPath, "-Quiet", .. extra
        ]);

    [Fact]
    public void TheJournalKeepsNonAsciiNamesAndApostrophesReadable()
    {
        Write("日本語のファイル.txt", "こんにちは世界。", "utf-8");
        Write("ملف-عربي.txt", "مرحبا بالعالم", "utf-8");
        Write("it's-a-file.txt", "plain ascii", "utf-8");

        Assert.Equal(0, Convert("-From", "utf-8"));

    // These files are read by a person recovering from a bad run, so the names have to
    // survive as names. The default encoder escapes every non-ASCII character and the
    // apostrophe too, turning the text EC exists to convert into \uXXXX.
    //
    // The assertion is on the raw bytes on disk, not on a deserialized value: the round
    // trip succeeds either way, so only the file itself shows whether it is readable.
        string json = File.ReadAllText(JournalPath);

        Assert.Contains("日本語のファイル.txt", json, StringComparison.Ordinal);
        Assert.Contains("ملف-عربي.txt", json, StringComparison.Ordinal);
        Assert.Contains("it's-a-file.txt", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ItRecordsWhatWasBelievedDecidedAndWritten()
    {
        const string text = "こんにちは世界。日本語のテキストです。";
        string path = Write("jp.txt", text, "shift_jis");
        string before = ConversionMetadataStore.ComputeSha256(path);

        Assert.Equal(0, Convert("-Backup", "-From", "shift_jis"));

        JournalEntry entry = EntryFor("jp.txt");

        // Believed.
        Assert.Equal("Explicit", entry.DetectionMode);
        Assert.Equal("shift_jis", entry.DetectedEncoding);
        Assert.Equal(932, entry.DetectedCodePage);
        Assert.False(entry.ExplicitSourceDiffersFromDetection);
        Assert.Equal(SourceInterpretation.ExplicitSource, entry.SourceInterpretation);

        // Decided.
        Assert.Equal(PlannedAction.Convert, entry.PlannedAction);

        // Written — and checkable against the disk, which is the point of recording it.
        Assert.Equal(ConversionStatus.Converted, entry.Status);
        Assert.Equal(before, entry.Sha256Before);
        Assert.Equal(ConversionMetadataStore.ComputeSha256(path), entry.Sha256After);
        Assert.NotEqual(entry.Sha256Before, entry.Sha256After);
        Assert.Equal("jp.txt.bak", entry.BackupPath);
        Assert.Equal("jp.txt.ecmeta.json", entry.RecoveryMetadataPath);
    }

    [Fact]
    public void ItRecordsTheEncodingTheConversionReadRatherThanWhatTheFileBecame()
    {
        // A completed conversion re-labels the entry so a second pass reads the new bytes
        // correctly, which means by journal time the file's effective encoding is the
        // target. Reporting that as what it was read as would invert what happened.
        Write("jp.txt", "こんにちは世界。日本語のテキストです。", "shift_jis");

        Assert.Equal(0, Convert("-From", "shift_jis"));

        JournalEntry entry = EntryFor("jp.txt");

        Assert.Equal("shift_jis", entry.SourceEncoding);
        Assert.Equal(932, entry.SourceCodePage);
        Assert.NotEqual("utf-8", entry.SourceEncoding);
    }

    [Fact]
    public void ARefusalIsRecordedAsLegacyGuidanceRatherThanProof()
    {
        // The question a record most often has to answer is why something was left alone.
        string path = Write("ambiguous.txt", "Le café était déjà prêt", "windows-1252");
        string before = ConversionMetadataStore.ComputeSha256(path);

        Assert.Equal(5, Convert());

        JournalEntry entry = EntryFor("ambiguous.txt");

        Assert.Equal(PlannedAction.Refuse, entry.PlannedAction);
        Assert.Equal(ConversionStatus.Refused, entry.Status);
        Assert.Equal(SourceInterpretation.LegacyNeedsSourceChoice, entry.SourceInterpretation);
        Assert.Equal(ConversionReasonCodes.LegacySourceRequired, entry.ReasonCode);
        Assert.Contains("EC converts automatically only from Unicode and ASCII", entry.Reason);

        // Nothing was written, so there is no "after" — and the file still is what the
        // "before" says it is.
        Assert.Null(entry.Sha256After);
        Assert.Equal(before, entry.Sha256Before);
        Assert.Equal(before, ConversionMetadataStore.ComputeSha256(path));
    }

    [Fact]
    public void AnExplicitSourceIsDistinguishedFromADetectedOne()
    {
        // Detection can be wrong in ways an explicit choice cannot. A record that cannot
        // tell them apart cannot say who was responsible for the reading.
        Write("ambiguous.txt", "Le café était déjà prêt", "windows-1252");

        Assert.Equal(0, Convert("-From", "windows-1252"));

        JournalEntry entry = EntryFor("ambiguous.txt");

        Assert.Equal("Explicit", entry.DetectionMode);
        Assert.Equal("windows-1252", entry.SourceEncoding);
        Assert.Equal(SourceInterpretation.ExplicitSource, entry.SourceInterpretation);
        Assert.Equal(ConversionStatus.Converted, entry.Status);
        Assert.Equal("windows-1252", Load().ExplicitSourceEncoding);
    }

    [Fact]
    public void AnExplicitSourceDisagreementIsRecordedWithoutRefusingLegacyConversion()
    {
        // This short byte sequence is reported as UTF-8 by the sample detector, while
        // full-file validation lets the user's explicit windows-1252 interpretation
        // proceed. The journal must retain both claims and identify their disagreement.
        Write("override.txt", "café", "windows-1252");

        Assert.Equal(0, Convert("-From", "windows-1252"));

        JournalEntry entry = EntryFor("override.txt");

        Assert.Equal(ConversionStatus.Converted, entry.Status);
        Assert.Equal("Explicit", entry.DetectionMode);
        Assert.Equal("utf-8", entry.DetectedEncoding);
        Assert.Equal(65001, entry.DetectedCodePage);
        Assert.Equal("windows-1252", entry.SourceEncoding);
        Assert.Equal(1252, entry.SourceCodePage);
        Assert.True(entry.ExplicitSourceDiffersFromDetection);
    }

    [Fact]
    public void CodecAliasesDoNotCreateAFalseExplicitSourceDisagreement()
    {
        string path = Write("alias.txt", "plain ascii", "ibm866");
        string hash = ConversionMetadataStore.ComputeSha256(path);
        var entry = new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = "ibm866",
            SourceHasBom = false,
            TargetEncoding = "utf-8",
            TargetHasBom = false,
            Result = ConversionRowResult.Unchanged,
            SourceEncodingWasSpecified = true,
            DetectedEncodingLabel = "cp866",
            ResolvedSourceLabel = "ibm866",
            JournalSourceSha256 = hash,
            Action = PlannedAction.Unchanged,
            SourceInterpretation = SourceInterpretation.ExplicitSource,
        };

        JournalEntry journalEntry = Assert.Single(
            ConversionJournal.FromRun(
                [entry], _root, "utf-8", targetHasBom: false,
                backupEnabled: false, explicitSource: "ibm866",
                surface: "Test", startedUtc: DateTime.UtcNow).Entries);

        Assert.Equal(866, journalEntry.DetectedCodePage);
        Assert.Equal(866, journalEntry.SourceCodePage);
        Assert.False(journalEntry.ExplicitSourceDiffersFromDetection);
    }

    [Fact]
    public void EveryFileTheRunTouchedIsAccountedFor()
    {
        Write("jp.txt", "こんにちは世界。日本語のテキストです。", "shift_jis");
        Write("ambiguous.txt", "Le café était déjà prêt", "windows-1252");
        Write("already.txt", "already utf-8 世界", "utf-8");

        Convert();

        ConversionJournal journal = Load();

        Assert.Equal(3, journal.Entries.Count);
        Assert.Equal(3, journal.Summary.Values.Sum());
        Assert.Equal(2, journal.Summary["Refused"]);
        Assert.Equal(1, journal.Summary["Unchanged"]);
    }

    [Fact]
    public void RewritingAJournalInsideTheScanRootNeverScansTheJournal()
    {
        Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");

        Assert.Equal(0, Convert("-From", "shift_jis"));
        Assert.Equal(0, Convert("-From", "utf-8"));

        ConversionJournal journal = Load();
        JournalEntry only = Assert.Single(journal.Entries);
        Assert.Equal("jp.txt", only.RelativePath);
    }

    [Fact]
    public void ItRecordsTheConversionBehaviourTheRunUsed()
    {
        // The same reason a plan carries it: what was done is only meaningful alongside
        // the rules it was done under.
        Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");

        Convert("-Backup", "-From", "shift_jis");

        ConversionJournal journal = Load();

        Assert.Equal(ConversionJournal.CurrentJournalVersion, journal.JournalVersion);
        Assert.Equal(ConversionSemantics.Current, journal.SemanticsVersion);
        Assert.True(journal.Semantics.StrictDecoding);
        Assert.True(journal.Semantics.LegacyRequiresExplicitSource);
        Assert.Equal("CommandLine", journal.Surface);
        Assert.Equal("utf-8", journal.TargetEncoding);
        Assert.False(journal.TargetHasBom);
        Assert.True(journal.BackupEnabled);
        Assert.NotEmpty(journal.EcVersion);
    }

    [Fact]
    public void ApplyingAPlanRecordsWhichPlanWasCarriedOut()
    {
        Write("jp.txt", "こんにちは世界。日本語のテキストです。", "shift_jis");

        string planPath = Path.Combine(_root, "plan.json");

        Assert.Equal(0, Cli(
            "-BasePath", _root, "-Target", "utf-8", "-From", "shift_jis", "-Plan", planPath, "-Quiet"));

        Assert.Equal(0, Cli("-Apply", planPath, "-Journal", JournalPath));

        ConversionJournal journal = Load();

        Assert.Equal(planPath, journal.AppliedPlan);
        Assert.Equal(
            ConversionStatus.Converted, Assert.Single(journal.Entries).Status);
    }

    [Fact]
    public void AFailedConversionIsNotRecordedAsARefusal()
    {
        // Both leave the file alone, and the difference is the whole of what a reader
        // needs: one is EC declining, the other is EC trying and not managing.
        byte[] original = Encoding.UTF8.GetBytes("世界 مرحبا");
        string path = Path.Combine(_root, "unencodable.txt");
        File.WriteAllBytes(path, original);

        Assert.Equal(3, Cli(
            "-BasePath", _root, "-Target", "windows-1252",
            "-Backup", "-Journal", JournalPath, "-Quiet"));

        JournalEntry entry = EntryFor("unencodable.txt");

        Assert.Equal(ConversionStatus.Failed, entry.Status);
        Assert.Equal(PlannedAction.Convert, entry.PlannedAction);
        Assert.Null(entry.Sha256After);
        Assert.Equal("unencodable.txt.bak", entry.BackupPath);
        Assert.Null(entry.RecoveryMetadataPath);
        Assert.Equal(original, File.ReadAllBytes(path + ".bak"));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void AnUnsupportedRecordedCodecCannotCrashJournalCreation()
    {
        string path = Write("unsupported.txt", "plain ascii", "ascii");
        var entry = new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = "utf-7",
            TargetEncoding = "utf-8",

            // Refused, not Error: this is what ConvertFiles actually records for a codec
            // the runtime cannot supply. The fixture previously paired Error with this
            // reason code, a combination production never produces, and relied on a
            // mapping that turned any Error on a refused entry into Refused - which is
            // what let a file EC could not even open be journaled as a policy decision.
            Result = ConversionRowResult.Refused,
            Action = PlannedAction.Refuse,
            SourceInterpretation = SourceInterpretation.NotApplicable,
            ResolvedSourceLabel = "utf-7",
            ReasonCode = ConversionReasonCodes.UnsupportedSourceEncoding,
        };

        JournalEntry recorded = Assert.Single(
            ConversionJournal.FromRun(
                [entry], _root, "utf-8", targetHasBom: false,
                backupEnabled: false, explicitSource: null,
                surface: "Test", startedUtc: DateTime.UtcNow).Entries);

        Assert.Equal(0, recorded.SourceCodePage);
        Assert.Equal(ConversionStatus.Refused, recorded.Status);
    }

    [Fact]
    public void AJournalIsRefusedForModesThatConvertNothing()
    {
        Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");

        Assert.Equal(1, Cli(
            "-BasePath", _root, "-DetectOnly", "-Journal", JournalPath));

        Assert.Equal(1, Cli(
            "-BasePath", _root, "-Validate", "utf-8", "-Journal", JournalPath));

        Assert.False(File.Exists(JournalPath));
    }

    [Fact]
    public void APreviewRecordsWhatWouldHaveHappenedWithoutClaimingItDid()
    {
        // -WhatIf reports rows as "would be converted". A journal of that run must not
        // read as a record of files having been rewritten.
        string path = Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");
        byte[] before = File.ReadAllBytes(path);

        Assert.Equal(0, Convert("-WhatIf", "-From", "shift_jis"));

        JournalEntry entry = EntryFor("jp.txt");

        // The decision is recorded; the outcome is not claimed.
        Assert.Equal(PlannedAction.Convert, entry.PlannedAction);
        Assert.Equal(ConversionStatus.NotAttempted, entry.Status);
        Assert.Null(entry.Sha256After);
        Assert.True(Load().Preview);

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(
            ConversionMetadataStore.ComputeSha256(path), entry.Sha256Before);
    }
}
