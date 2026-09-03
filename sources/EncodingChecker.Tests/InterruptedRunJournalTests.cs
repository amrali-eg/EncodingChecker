using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// Cancelling a conversion that has already written files must still record what it did.
/// </summary>
/// <remarks>
/// Cancellation left <see cref="ConversionOrchestrator.Run"/> by exception, so the
/// orchestration result was never built and the journal went with it. Files converted
/// before the cancellation stayed converted with nothing recording the writes - the one
/// case where the audit trail is most needed is the one where it did not exist.
/// <para>
/// There is a trap underneath: an entry keeps the deciding pass's result until the write
/// pass overwrites it, so a file the run never reached still reads as Converted. A
/// journal built without allowing for that reports conversions that never happened,
/// which is worse than no journal at all.
/// </para>
/// </remarks>
public sealed class InterruptedRunJournalTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_interrupted_").FullName;

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

    /// <summary>A UTF-8 file with a BOM, so converting to utf-8 rewrites it.</summary>
    private string Write(string name)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, $"content of {name}", new UTF8Encoding(true));

        return path;
    }

    private static bool StillHasBom(string path) =>
        File.ReadAllBytes(path).Take(3).SequenceEqual(Encoding.UTF8.GetPreamble());

    private List<ConversionReportEntry> Scan()
    {
        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Detect,
        };

        var sink = new EntrySink();
        ScanEngine.ScanDirectory(options, sink.Add, CancellationToken.None);

        return sink.ToList();
    }

    /// <summary>
    /// Runs a conversion that cancels itself once <paramref name="stopAfter"/> files
    /// have been written.
    /// </summary>
    private OrchestrationResult RunCancellingAfter(
        int stopAfter,
        List<ConversionReportEntry> entries,
        out List<string> converted,
        int maxParallelism = 1)
    {
        using var cancellation = new CancellationTokenSource();
        var written = new List<string>();
        object gate = new();

        var orchestrator = new ConversionOrchestrator(_ => ConfirmationResponse.Proceed);

        OrchestrationResult result = orchestrator.Run(
            entries,
            _root,
            "utf-8",
            targetWriteBom: false,
            backup: false,
            preview: false,
            maxParallelism,
            onEntry: entry =>
            {
                lock (gate)
                {
                    if (entry.Result == ConversionRowResult.Converted)
                        written.Add(entry.FilePath);

                    if (written.Count >= stopAfter)
                        cancellation.Cancel();
                }
            },
            cancellation.Token);

        converted = written;

        return result;
    }

    [Fact]
    public void ACancelledRunStillProducesAJournalOfWhatItWrote()
    {
        for (int i = 1; i <= 6; i++)
            Write($"f{i}.txt");

        List<ConversionReportEntry> entries = Scan();

        OrchestrationResult result =
            RunCancellingAfter(2, entries, out List<string> converted);

        Assert.Equal(OrchestrationOutcome.Interrupted, result.Outcome);

        // The point of the fix: the record of the writes survives the cancellation.
        Assert.NotNull(result.Journal);
        Assert.Equal(entries.Count, result.Journal!.Entries.Count);

        Assert.All(
            converted,
            path => Assert.False(
                StillHasBom(path), "a file reported converted was not rewritten"));
    }

    [Fact]
    public void FilesTheRunNeverReachedAreNotReportedAsConverted()
    {
        // The trap. Every entry arrives at the write pass already marked Converted by
        // the deciding pass, so a journal that trusts that reports the whole batch as
        // done however early the cancellation landed.
        for (int i = 1; i <= 6; i++)
            Write($"f{i}.txt");

        List<ConversionReportEntry> entries = Scan();

        OrchestrationResult result =
            RunCancellingAfter(2, entries, out List<string> converted);

        ConversionJournal journal = result.Journal!;

        int reportedConverted = journal.Entries.Count(
            e => e.Status == ConversionStatus.Converted);

        int reportedNotAttempted = journal.Entries.Count(
            e => e.Status == ConversionStatus.NotAttempted);

        Assert.Equal(converted.Count, reportedConverted);
        Assert.Equal(entries.Count - converted.Count, reportedNotAttempted);

        // And the files it never reached still hold their original bytes, which is what
        // makes NotAttempted the true statement rather than merely the cautious one.
        foreach (JournalEntry entry in journal.Entries
                     .Where(e => e.Status == ConversionStatus.NotAttempted))
        {
            Assert.True(StillHasBom(Path.Combine(_root, entry.RelativePath)));
            Assert.Null(entry.Sha256After);
        }
    }

    [Fact]
    public void ParallelCancellationAccountsForEveryCompletedCallback()
    {
        // ConvertFiles invokes callbacks concurrently. Completion tracking must therefore
        // remain complete even when several workers finish while cancellation propagates.
        for (int i = 1; i <= 64; i++)
            Write($"parallel-{i:D2}.txt");

        List<ConversionReportEntry> entries = Scan();

        OrchestrationResult result =
            RunCancellingAfter(2, entries, out List<string> converted, maxParallelism: 8);

        Assert.Equal(OrchestrationOutcome.Interrupted, result.Outcome);
        Assert.NotEmpty(converted);

        ConversionJournal journal = result.Journal!;
        Assert.Equal(entries.Count, journal.Entries.Count);

        HashSet<string> convertedNames =
        [
            .. converted.Select(path => Path.GetFileName(path)!)
        ];

        foreach (JournalEntry item in journal.Entries)
        {
            if (convertedNames.Contains(item.RelativePath))
            {
                Assert.Equal(ConversionStatus.Converted, item.Status);
                Assert.False(StillHasBom(Path.Combine(_root, item.RelativePath)));
            }
            else
            {
                Assert.Equal(ConversionStatus.NotAttempted, item.Status);
                Assert.True(StillHasBom(Path.Combine(_root, item.RelativePath)));
            }
        }
    }

    [Fact]
    public void AnUninterruptedRunIsStillReportedAsConverted()
    {
        // The control. Returning Interrupted unconditionally, or marking everything
        // NotAttempted, would satisfy both tests above.
        Write("only.txt");

        List<ConversionReportEntry> entries = Scan();

        using var cancellation = new CancellationTokenSource();
        var orchestrator = new ConversionOrchestrator(_ => ConfirmationResponse.Proceed);

        OrchestrationResult result = orchestrator.Run(
            entries, _root, "utf-8", targetWriteBom: false, backup: false,
            preview: false, maxParallelism: 1, onEntry: _ => { }, cancellation.Token);

        Assert.Equal(OrchestrationOutcome.Converted, result.Outcome);

        JournalEntry recorded = Assert.Single(result.Journal!.Entries);

        Assert.Equal(ConversionStatus.Converted, recorded.Status);
        Assert.NotNull(recorded.Sha256After);
        Assert.False(StillHasBom(Path.Combine(_root, "only.txt")));
    }

    [Fact]
    public void RetryingAnInterruptedRunDoesNotKeepNotAttemptedStatuses()
    {
        // The same rows remain in the GUI after interruption. A retry must replace the
        // first run's NotAttempted markers with what the second run actually did.
        for (int i = 1; i <= 6; i++)
            Write($"retry-{i}.txt");

        List<ConversionReportEntry> entries = Scan();

        OrchestrationResult interrupted =
            RunCancellingAfter(2, entries, out _);

        Assert.Equal(OrchestrationOutcome.Interrupted, interrupted.Outcome);
        Assert.Contains(entries, entry => entry.NotAttempted);

        var retry = new ConversionOrchestrator(_ => ConfirmationResponse.Proceed);

        OrchestrationResult completed = retry.Run(
            entries, _root, "utf-16", targetWriteBom: true, backup: false,
            preview: false, maxParallelism: 1, onEntry: _ => { },
            CancellationToken.None);

        Assert.Equal(OrchestrationOutcome.Converted, completed.Outcome);
        Assert.All(entries, entry => Assert.False(entry.NotAttempted));
        Assert.All(
            completed.Journal!.Entries,
            entry => Assert.Equal(ConversionStatus.Converted, entry.Status));
    }

    [Fact]
    public void DecliningAtTheReviewIsStillReportedAsUntouched()
    {
        // Interrupted must not swallow the outcome it was split away from: declining
        // the confirmation writes nothing and has no journal to keep.
        Write("declined.txt");

        List<ConversionReportEntry> entries = Scan();

        var orchestrator = new ConversionOrchestrator(_ => ConfirmationResponse.Cancel);

        OrchestrationResult result = orchestrator.Run(
            entries, _root, "utf-8", targetWriteBom: false, backup: false,
            preview: false, maxParallelism: 1, onEntry: _ => { },
            CancellationToken.None);

        Assert.Equal(OrchestrationOutcome.Cancelled, result.Outcome);
        Assert.Null(result.Journal);
        Assert.True(StillHasBom(Path.Combine(_root, "declined.txt")));
    }
}
