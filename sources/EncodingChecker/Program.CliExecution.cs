using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace EncodingChecker;

internal static partial class Program
{
    private static int ApplyPlan(CliOptions options)
    {
        ConversionPlan? plan = ConversionPlan.Load(options.ApplyPath!, out string? loadError);

        if (plan is null)
        {
            Console.Error.WriteLine($"The plan could not be read: {loadError}");
            return 1;
        }

        // The plan's relative paths can only be resolved against its recorded root.
        if (!Directory.Exists(plan.BaseDirectory))
        {
            Console.Error.WriteLine(
                $"The plan's directory no longer exists: {plan.BaseDirectory}");
            return 3;
        }

        // A reparse point can redirect the approved path to an unreviewed tree while all
        // file hashes still match. FindStaleFiles repeats this check for each entry.
        if (DirectoryTraversal.IsReparsePointDirectory(plan.BaseDirectory))
        {
            Console.Error.WriteLine(
                $"The plan's directory is now a symbolic link or other reparse point, " +
                $"so it may no longer be the directory that was reviewed: " +
                $"{plan.BaseDirectory}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "Re-run -Plan against the real directory you intend to convert.");
            return 3;
        }

        // Apply the reviewed plan; do not re-detect and silently change its decisions.
        IReadOnlyList<string> stale = plan.FindStaleFiles();

        if (stale.Count > 0)
        {
            Console.Error.WriteLine(
                "The plan no longer describes these files, so nothing was converted:");

            foreach (string entry in stale.Take(20))
                Console.Error.WriteLine($"  {entry}");

            if (stale.Count > 20)
                Console.Error.WriteLine($"  ...and {stale.Count - 20} more.");

            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "Re-run -Plan to produce a plan for the files as they are now.");
            return 3;
        }

        DateTime startedUtc = DateTime.UtcNow;

        // Materialize every plan entry. ConvertFiles preserves Refuse, Skip, and
        // Unchanged decisions without writing them; including them lets the journal
        // account for the complete reviewed plan rather than only successful work.
        List<ConversionReportEntry> entries =
        [
            .. plan.Files
                .Select(f => new ConversionReportEntry
                {
                    FilePath = plan.ResolvePath(f)!,
                    SourceEncoding = f.SourceEncoding,
                    SourceHasBom = f.SourceHasBom,
                    TargetEncoding = plan.TargetEncoding,
                    TargetHasBom = plan.TargetHasBom,

                    // Carry the approved decision and action into the write pass.
                    Action = f.Action,
                    SourceInterpretation = f.SourceInterpretation,
                    SourceEncodingWasSpecified = f.SourceWasSpecified,
                    DetectedEncodingLabel = f.DetectedEncoding,
                    DetectedEncodingHasBom = f.DetectedHasBom,
                    ReasonCode = f.ReasonCode,
                    Diagnostic = f.Reason,

                    // A policy input, not provenance: without it the explicit-source
                    // veto cannot fire when the plan is applied.
                    HasReliableUnicodeDetection = f.HasReliableUnicodeDetection,

                    // The reviewed decision, kept intact so re-deciding below can only
                    // refuse more than the review did, never less.
                    Approved = new ApprovedDecision(
                        f.Action,
                        f.SourceInterpretation,
                        f.ReasonCode,
                        f.Reason),

                    // Rechecked at installation so a long conversion cannot install over
                    // bytes that changed after the initial stale-file check.
                    ExpectedSourceSha256 = f.Sha256,
                })
        ];

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            ScanEngine.ConvertFiles(
                entries,
                plan.TargetEncoding,
                plan.TargetHasBom,
                options.MaxParallelism ?? ScanEngine.DefaultMaxParallelism,
                whatIf: false,
                backup: plan.BackupEnabled,
                _ => { },
                cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 4;
        }

        List<ConversionReportEntry> completed =
        [
            .. entries.OrderBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase)
        ];

        foreach (ConversionReportEntry entry in completed
                     .Where(e => e.Result == ConversionRowResult.Error))
        {
            Console.Error.WriteLine($"Error: {entry.FilePath}: {entry.Diagnostic}");
        }

        Dictionary<ConversionRowResult, int> byResult =
            completed.GroupBy(e => e.Result).ToDictionary(g => g.Key, g => g.Count());

        int Count(ConversionRowResult result) => byResult.GetValueOrDefault(result);

        int failed = Count(ConversionRowResult.Error);
        bool runFailed = failed > 0;

        // Account for every planned file in the journal, including ones left unchanged.
        if (!string.IsNullOrWhiteSpace(options.JournalPath))
        {
            string? journalError = ConversionJournal.FromRun(
                    completed,
                    plan.BaseDirectory,
                    plan.TargetEncoding,
                    plan.TargetHasBom,
                    plan.BackupEnabled,
                    plan.ExplicitSourceEncoding,
                    surface: "CommandLine",
                    startedUtc,
                    appliedPlan: options.ApplyPath)
                .Save(options.JournalPath!);

            if (journalError != null)
            {
                Console.Error.WriteLine(
                    "The conversion ran, but the journal could not be written: "
                    + journalError);
                runFailed = true;
            }
        }

        if (!options.Quiet)
        {
            Console.Out.WriteLine(
                $"Applied plan: {plan.Files.Count} selected, "
                + $"{Count(ConversionRowResult.Converted)} converted, "
                + $"{Count(ConversionRowResult.Unchanged)} unchanged, "
                + $"{Count(ConversionRowResult.Skipped)} skipped, "
                + $"{Count(ConversionRowResult.Refused)} refused, "
                + $"{failed} failed.");
        }

        return runFailed
            ? 3
            : completed.Any(e => e.Result == ConversionRowResult.Refused) ? 5 : 0;
    }

    // Internal so tests can pin the published CLI exit-code contract.
    internal static int RunConsoleMode(string[] args)
    {
        if (args is ["--version"])
        {
            Console.Out.WriteLine(GetDisplayVersion());
            return 0;
        }

        if (args is ["/?" or "-?" or "/h" or "-h" or "--help"])
        {
            Console.Out.WriteLine(UsageText);
            return 0;
        }

        if (!TryParseArguments(args, out CliOptions options, out string? parseError))
        {
            Console.Error.WriteLine(parseError);
            Console.Error.WriteLine();
            Console.Error.WriteLine(UsageText);
            return 1;
        }

        if (!TryValidateOptions(options, out string? validationError))
        {
            Console.Error.WriteLine(validationError);
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(options.ApplyPath))
            return ApplyPlan(options);

        // A plan is a written preview, so it must never modify files.
        if (!string.IsNullOrWhiteSpace(options.PlanPath))
            options.WhatIf = true;

        ScanAction action = options.DetectOnly
            ? ScanAction.Detect
            : options.ValidateCharsets != null
                ? ScanAction.Validate
                : ScanAction.Convert;

        List<string>? validCharsets = null;
        if (action == ScanAction.Validate)
        {
            validCharsets =
            [
                .. options.ValidateCharsets!
                    .Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries)
            ];
        }

        string? targetCharset = null;
        bool targetWriteBom = false;

        if (action == ScanAction.Convert)
        {
            ScanEngine.ParseCharsetLabel(
                options.Target!,
                out targetCharset,
                out targetWriteBom);
        }

        // Counts files the scan never examined so the run can say so rather than
        // letting a clean result stand in for complete coverage.
        var traversalCounters = new DirectoryTraversal.TraversalCounters();

        var scanOptions = new ScanDirectoryOptions
        {
            Counters = traversalCounters,
            SourceCharset = options.From,
            BaseDirectory = options.BasePath!,
            IncludeSubdirectories = true,
            IncludePatterns = options.Include,
            ExcludePatterns = options.Exclude,
            ExcludedFullPaths = GetOutputExclusions(options),
            Action = action,
            MaxParallelism =
                options.MaxParallelism ??
                ScanEngine.DefaultMaxParallelism,
            ValidCharsets = validCharsets,
            TargetCharset = targetCharset,
            TargetWriteBom = targetWriteBom,
            WhatIf = options.WhatIf,
            Backup = options.Backup,

            // Capture originals before conversion when a journal needs them.
            CaptureSourceHashes = !string.IsNullOrWhiteSpace(options.JournalPath),
        };

        // Worker callbacks are concurrent, so collect first and sort once at the end.
        var collectedEntries = new ConcurrentBag<ConversionReportEntry>();

        DateTime startedUtc = DateTime.UtcNow;

        using var cancellation = new CancellationTokenSource();

        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            // Let the scan observe cancellation and unwind safely.
            e.Cancel = true;
            cancellation.Cancel();
        };

        Console.CancelKeyPress += cancelHandler;

        try
        {
            ScanEngine.ScanDirectory(
                scanOptions,
                collectedEntries.Add,
                cancellation.Token,
                onWarning: Console.Error.WriteLine);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Scan cancelled.");
            return 4;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Scan failed: {ex.Message}");
            return 3;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        List<ConversionReportEntry> entries =
        [
            .. collectedEntries.OrderBy(
                e => e.FilePath,
                StringComparer.OrdinalIgnoreCase)
        ];

        if (options.Quiet)
        {
            Console.Out.WriteLine($"{entries.Count} file(s) processed.");
        }
        else
        {
            ConversionReport.WriteCsv(entries, Console.Out);
        }

        // Coverage, not a result: a caller reading only the rows cannot tell a clean
        // folder from one holding files the scan never opened. Written to stderr so it
        // survives -Quiet and stays out of the machine-readable report on stdout.
        if (traversalCounters.FilesExcludedByAttribute > 0)
        {
            Console.Error.WriteLine(
                $"{traversalCounters.FilesExcludedByAttribute} matching file(s) not examined "
                + "(hidden, system, or reparse point).");
        }

        if (traversalCounters.DirectoriesExcludedByAttribute > 0)
        {
            Console.Error.WriteLine(
                $"{traversalCounters.DirectoriesExcludedByAttribute} folder(s) not entered "
                + "(hidden, system, or reparse point); their contents were not counted.");
        }

        if (options.Verbose)
            PrintVerboseSummary(entries);

        if (!string.IsNullOrWhiteSpace(options.JournalPath))
        {
            string? journalError = ConversionJournal.FromRun(
                    entries,
                    options.BasePath!,
                    targetCharset ?? options.Target ?? string.Empty,
                    targetWriteBom,
                    options.Backup,
                    options.From,
                    surface: "CommandLine",
                    startedUtc,
                    appliedPlan: null,
                    preview: options.WhatIf)
                .Save(options.JournalPath!);

            if (journalError != null)
            {
                Console.Error.WriteLine(
                    $"Failed to write the journal: {journalError}");
                return 3;
            }
        }

        if (!string.IsNullOrEmpty(options.ReportPath))
        {
            try
            {
                using var writer = new StreamWriter(
                    options.ReportPath, false, ConversionReport.CsvFileEncoding);
                ConversionReport.WriteCsv(entries, writer);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(
                    $"Failed to write report file: {ex.Message}");
                return 3;
            }
        }

        if (!string.IsNullOrWhiteSpace(options.PlanPath))
        {
            ConversionPlan plan;

            try
            {
                plan = ConversionPlan.FromEntries(
                    entries,
                    options.BasePath!,
                    targetCharset!,
                    targetWriteBom,
                    options.Backup,
                    options.From);
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"Could not create conversion plan: {ex.Message}");
                return 3;
            }

            string? saveError = plan.Save(options.PlanPath!);

            if (saveError != null)
            {
                Console.Error.WriteLine($"Failed to write the plan: {saveError}");
                return 3;
            }

            if (!options.Quiet)
            {
                Console.Out.WriteLine();
                Console.Out.WriteLine(plan.Summarize());
            }

            // A refusal is an expected preflight result, not a failed preflight.
            if (options.FailOnChanges &&
                plan.Files.Any(f => f.Action == PlannedAction.Convert))
            {
                return 2;
            }

            return 0;
        }

        if (entries.Any(e => e.Result == ConversionRowResult.Error))
            return 3;

        if (entries.Any(e => e.Result == ConversionRowResult.Refused))
            return 5;

        if (options is { FailOnChanges: true, DetectOnly: false })
        {
            bool changesFound = action == ScanAction.Validate
                ? entries.Any(e => e.Result == ConversionRowResult.Invalid)
                : entries.Any(e => e.Result == ConversionRowResult.Converted);

            if (changesFound)
                return 2;
        }

        return 0;
    }

    private static IReadOnlyCollection<string> GetOutputExclusions(CliOptions options)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Add(options.ReportPath);
        Add(options.PlanPath);
        Add(options.JournalPath);

        return paths;

        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                paths.Add(Path.GetFullPath(path));
        }
    }
}
