using System.Text;
using System.Text.Json;
using static EncodingChecker.Tests.CliRunner;
using static EncodingChecker.Tests.ExpectedExitCode;

namespace EncodingChecker.Tests;

/// <summary>
/// A file that changes after its conversion was decided keeps its existing backup and
/// recovery record, because the backup must hold the bytes that were approved.
/// </summary>
/// <remarks>
/// A plan's stale check runs once, before any file is written. A file changed after that check
/// but before its own turn was refused by the converter's later hash check, yet its backup had
/// already been replaced with the changed bytes and its recovery record deleted: an earlier
/// restore point was lost for a conversion that never happened. Each case changes the second
/// file only after the first has finished, so the stale check has already passed. The cases
/// cover a change made before the backup is staged, not one made after it.
/// </remarks>
public sealed class BackupBoundToApprovedBytesTests : IDisposable
{
    private static readonly byte[] EarlierBackup =
        Encoding.ASCII.GetBytes("backup of an earlier original\r\n");

    private static readonly byte[] EarlierRecord =
        Encoding.ASCII.GetBytes("{\"sentinel\":\"recovery record of that backup\"}");

    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_backup_bound_").FullName;

    private readonly string _outputs =
        Directory.CreateTempSubdirectory("ec_backup_bound_out_").FullName;

    public void Dispose()
    {
        foreach (string directory in new[] { _root, _outputs })
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    private string PathOf(string name) => Path.Combine(_root, name);

    private static string RecordOf(string path) => ConversionMetadataStore.MetadataPathFor(path);

    // Two convertible files, each with a backup and record left by an earlier run.
    private void WriteFilesWithEarlierBackups()
    {
        foreach (string name in new[] { "a.txt", "b.txt" })
        {
            string path = PathOf(name);
            File.WriteAllText(path, $"text of {name}\r\n", Encoding.Unicode);
            File.WriteAllBytes(path + ".bak", EarlierBackup);
            File.WriteAllBytes(RecordOf(path), EarlierRecord);
        }
    }

    // Changes whichever file the run has not reached yet, once the first one has finished.
    private sealed class ChangeTheOtherFileAfterTheFirst(string root)
    {
        internal string? Changed { get; private set; }

        internal byte[] ChangedBytes { get; } =
            Encoding.Unicode.GetBytes("changed after the stale check\r\n");

        internal void OnEntry(ConversionReportEntry entry)
        {
            if (Changed is not null)
                return;

            Changed = Path.Combine(
                root, Path.GetFileName(entry.FilePath) == "a.txt" ? "b.txt" : "a.txt");
            File.WriteAllBytes(Changed, ChangedBytes);
        }
    }

    private void AssertEarlierBackupKept(ChangeTheOtherFileAfterTheFirst change)
    {
        Assert.NotNull(change.Changed);
        Assert.Equal(change.ChangedBytes, File.ReadAllBytes(change.Changed));
        Assert.Equal(EarlierBackup, File.ReadAllBytes(change.Changed + ".bak"));
        Assert.Equal(EarlierRecord, File.ReadAllBytes(RecordOf(change.Changed)));
        Assert.Empty(Directory.GetFiles(_root, "*." + EncodingConverter.TempFileSuffix));
    }

    private static void AssertRefusedWithoutABackup(ConversionReportEntry entry)
    {
        Assert.Equal(ConversionRowResult.Error, entry.Result);
        Assert.Equal(
            nameof(ConversionErrorCode.SourceChangedDuringConversion), entry.ReasonCode);
        Assert.Contains("existing backup and recovery record were left in place", entry.Diagnostic);
        Assert.Null(entry.BackupPath);
        Assert.Null(entry.RecoveryMetadataPath);
        Assert.False(entry.ReplacementCommitted);
    }

    private string MakePlan()
    {
        string planPath = Path.Combine(_outputs, "plan.json");
        Assert.Equal(
            ExpectedClean,
            Run("-BasePath", _root, "-Target", "utf-8", "-Backup", "-Plan", planPath, "-Quiet"));

        return planPath;
    }

    [Fact]
    public void ApplyingAPlanKeepsTheEarlierBackupOfAFileChangedAfterTheStaleCheck()
    {
        WriteFilesWithEarlierBackups();
        string planPath = MakePlan();
        string journalPath = Path.Combine(_outputs, "journal.json");
        var change = new ChangeTheOtherFileAfterTheFirst(_root);
        var reached = new EntrySink();

        (int exit, _, _) = RunCapturedWithCancellation(
            ["-Apply", planPath, "-MaxParallelism", "1", "-Journal", journalPath, "-Quiet"],
            CancellationToken.None,
            entry =>
            {
                reached.Add(entry);
                change.OnEntry(entry);
            });

        Assert.Equal(2, reached.Count());
        Assert.Equal(ExpectedProcessingErrors, exit);
        AssertEarlierBackupKept(change);
        AssertRefusedWithoutABackup(
            Assert.Single(reached, entry => entry.FilePath == change.Changed));

        // The journal does not claim a backup for the refused file.
        using JsonDocument journal = JsonDocument.Parse(File.ReadAllText(journalPath));
        JsonElement refused = Assert.Single(
            journal.RootElement.GetProperty("Entries").EnumerateArray(),
            e => e.GetProperty("RelativePath").GetString() == Path.GetFileName(change.Changed));

        Assert.Equal(
            nameof(ConversionErrorCode.SourceChangedDuringConversion),
            refused.GetProperty("ReasonCode").GetString());
        Assert.Equal(JsonValueKind.Null, refused.GetProperty("BackupPath").ValueKind);
        Assert.Equal(JsonValueKind.Null, refused.GetProperty("RecoveryMetadataPath").ValueKind);
    }

    [Fact]
    public void TheGuiKeepsTheEarlierBackupOfAFileChangedAfterTheStaleCheck()
    {
        WriteFilesWithEarlierBackups();

        var scanned = new EntrySink();
        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                IncludeSubdirectories = true,
                IncludePatterns = ["*"],
                Action = ScanAction.Detect,
            },
            scanned.Add,
            CancellationToken.None);

        List<ConversionReportEntry> rows = [.. scanned];
        Assert.Equal(2, rows.Count);

        var change = new ChangeTheOtherFileAfterTheFirst(_root);

        new ConversionOrchestrator(_ => ConfirmationResponse.Proceed).Run(
            rows, _root, "utf-8", targetWriteBom: false,
            backup: true, preview: false,
            maxParallelism: 1,
            change.OnEntry,
            CancellationToken.None);

        AssertEarlierBackupKept(change);
        AssertRefusedWithoutABackup(Assert.Single(rows, row => row.FilePath == change.Changed));
    }

    [Fact]
    public void AnUnchangedFileStillReplacesItsBackupWithTheApprovedBytes()
    {
        // The control: with nothing changed, both files convert and each backup now holds
        // the bytes the plan approved, with a new recovery record beside it.
        WriteFilesWithEarlierBackups();
        byte[] aOriginal = File.ReadAllBytes(PathOf("a.txt"));
        byte[] bOriginal = File.ReadAllBytes(PathOf("b.txt"));
        string planPath = MakePlan();

        Assert.Equal(ExpectedClean, Run("-Apply", planPath, "-MaxParallelism", "1", "-Quiet"));

        Assert.Equal(aOriginal, File.ReadAllBytes(PathOf("a.txt.bak")));
        Assert.Equal(bOriginal, File.ReadAllBytes(PathOf("b.txt.bak")));
        Assert.NotEqual(EarlierRecord, File.ReadAllBytes(RecordOf(PathOf("a.txt"))));
        Assert.NotEqual(EarlierRecord, File.ReadAllBytes(RecordOf(PathOf("b.txt"))));
        Assert.Equal("text of a.txt\r\n", File.ReadAllText(PathOf("a.txt"), new UTF8Encoding(false)));
    }
}
