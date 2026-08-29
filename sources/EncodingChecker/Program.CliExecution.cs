using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
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

                    // Rechecked at installation so a long conversion cannot install over
                    // bytes that changed after the initial stale-file check.
                    ExpectedSourceSha256 = f.Sha256,
                })
        ];

        // ConvertFiles invokes callbacks concurrently. A plain List can silently lose
        // entries, which would make the report and journal incomplete after a real run.
        var completedSink = new ConcurrentBag<ConversionReportEntry>();

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
                completedSink.Add,
                cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 4;
        }

        List<ConversionReportEntry> completed =
        [
            .. completedSink.OrderBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase)
        ];

        foreach (ConversionReportEntry entry in completed
                     .Where(e => e.Result == ConversionRowResult.Error))
        {
            Console.Error.WriteLine($"Error: {entry.FilePath}: {entry.Diagnostic}");
        }

        Dictionary<ConversionRowResult, int> byResult =
            completed.GroupBy(e => e.Result).ToDictionary(g => g.Key, g => g.Count());

        int Count(ConversionRowResult result) => byResult.GetValueOrDefault(result);

        // A planned refusal is an expected safety outcome, not an apply failure. Other
        // errors mean an approved conversion could not complete.
        int failed = completed.Count(
            entry => entry.Result == ConversionRowResult.Error &&
                     entry.Action != PlannedAction.Refuse);

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
                failed++;
            }
        }

        Console.Out.WriteLine(
            $"Applied plan: {Count(ConversionRowResult.Converted)} converted, "
            + $"{Count(ConversionRowResult.Unchanged)} already in the target encoding, "
            + $"{Count(ConversionRowResult.Skipped)} skipped, "
            + $"{failed} failed, "
            + $"{plan.Files.Count - entries.Count} not scheduled for conversion.");

        return failed > 0 ? 3 : 0;
    }

    // Internal so tests can pin the published CLI exit-code contract.
    internal static int RunConsoleMode(string[] args)
    {
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

        var scanOptions = new ScanDirectoryOptions
        {
            SourceCharset = options.From,
            BaseDirectory = options.BasePath!,
            IncludeSubdirectories = true,
            IncludePatterns = options.Include,
            ExcludePatterns = options.Exclude,
            ExcludedFullPath = string.IsNullOrEmpty(options.ReportPath)
                ? null
                : Path.GetFullPath(options.ReportPath),
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
                using var writer = new StreamWriter(options.ReportPath);
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
            ConversionPlan plan = ConversionPlan.FromEntries(
                entries,
                options.BasePath!,
                targetCharset!,
                targetWriteBom,
                options.Backup,
                options.From);

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
}
