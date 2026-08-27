using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace EncodingChecker;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Enable legacy code pages used by the detector and converter.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (args.Length > 0)
        {
            bool attachedConsole = TryAttachToParentConsole();

            // Only an interactive console needs padding: the shell doesn't wait for a
            // WinExe process, so its prompt is already drawn where we're about to write.
            bool needsPromptPadding = attachedConsole && !Console.IsOutputRedirected;

            if (needsPromptPadding)
                Console.Out.WriteLine();

            int exitCode = RunConsoleMode(args);

            if (needsPromptPadding)
                Console.Out.WriteLine();

            return exitCode;
        }

        Application.ThreadException += OnApplicationThreadException;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }

    private static void OnApplicationThreadException(object sender, ThreadExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.Message,
            @"Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    #region Console attachment

    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);

    /// <summary>
    /// Attaches to the parent console for CLI launches. Required because a WinExe
    /// does not automatically acquire the newly attached console's standard streams.
    /// </summary>
    /// <returns><see langword="true"/> if a parent console was attached.</returns>
    private static bool TryAttachToParentConsole()
    {
        if (!AttachConsole(ATTACH_PARENT_PROCESS))
            return false;

        var stdout = new StreamWriter(Console.OpenStandardOutput())
        {
            AutoFlush = true
        };
        Console.SetOut(stdout);

        var stderr = new StreamWriter(Console.OpenStandardError())
        {
            AutoFlush = true
        };
        Console.SetError(stderr);

        return true;
    }

    #endregion

    #region Console mode

    // internal (not private) so EncodingChecker.Tests can exercise argument parsing
    // directly via InternalsVisibleTo, instead of only through process-level RunConsoleMode.
    internal sealed class CliOptions
    {
        internal string? BasePath;
        internal List<string> Include = [];
        internal List<string> Exclude = [];
        internal string? Target;
        internal string? From;
        internal string? PlanPath;
        internal string? ApplyPath;
        internal string? ValidateCharsets;
        internal bool DetectOnly;
        internal string? ReportPath;
        internal int? MaxParallelism;
        internal bool FailOnChanges;
        internal bool WhatIf;
        internal bool Backup;
        internal bool Quiet;
        internal bool Verbose;
    }

    private const string UsageText = """
        EncodingChecker v3.7.0

        Usage:

          EncodingChecker.exe
              -BasePath <directory>
              [-Include "<pattern1,pattern2,...>"]   # repeatable; patterns accumulate
              [-Exclude "<pattern1,pattern2,...>"]   # repeatable; patterns accumulate
                   Wildcard patterns. A pattern with no "/" or "\"
                   matches just the filename, at any depth. One
                   containing a separator matches the path relative
                   to -BasePath instead (e.g. "src/*.cs"); "/" and
                   "\" behave the same way. Exclusions are applied
                   after -Include. The following directories are always
                   skipped: .git, .svn, .hg, .vs, .idea, bin, obj,
                   node_modules, packages, dist, build, target.

              Conversion:
              [-Target "<encoding>"]
                   The name of the encoding to convert files to, e.g.
                   "utf-8" or "utf-8-bom". Required unless -Validate or
                   -DetectOnly is given.

              [-From "<encoding>"]
                   Treat every file as this encoding instead of
                   detecting it. Use when detection reports that a
                   file's encoding cannot be determined from its
                   contents, or when you already know it.

                   This replaces detection and nothing else: the bytes
                   must still decode strictly as this encoding, the
                   output is still verified to hold exactly the same
                   text, and a failed backup still aborts. Convert mode
                   only.

              Preflight:
              [-Plan <path>]
                   Write a conversion plan and change nothing. The plan
                   records, for every file, what would happen and why:
                   the encoding, whether it was detected or specified,
                   whether the bytes identify it uniquely, and which
                   files could come out with different text.

                   It also records the conversion itself - directory,
                   target encoding, BOM policy, backup policy, and the
                   guarantees this build provides - so the file is the
                   whole approval and needs no other options to mean
                   something exact.

              [-Apply <path>]
                   Carry out a plan written by -Plan. Every file is
                   checked against the hash it had when the plan was
                   made; if any has changed, nothing is converted at
                   all. A plan approved for one set of files is not
                   applied to a different one. Each file is checked
                   once more at the moment it is installed.

                   Nothing is detected a second time: the encodings,
                   the target, and the backup setting all come from the
                   plan, so -BasePath, -Target, -From, and -Backup are
                   rejected here rather than silently ignored. A plan
                   written under different conversion behaviour, or by
                   an incompatible schema, is refused rather than
                   guessed at.

              Modes:
                   Conversion is the default mode.
              [-Validate "<charset1,charset2,...>"]
                   Validate without writing anything. Files whose current
                   encoding isn't in this list are reported as Invalid.
                   May be combined with -FailOnChanges. Cannot be
                   combined with -DetectOnly or -Target.
              [-DetectOnly]
                   Read-only detection mode. Writes
                   File,Encoding,BOM,Target,TargetBOM,Result CSV rows to
                   stdout, where BOM is the source's BOM state and
                   TargetBOM the target's (Target/TargetBOM/Result always
                   mirror the source here, since nothing changes).
                   Nothing is modified. -Target, -WhatIf, -Backup,
                   -FailOnChanges, -Quiet, and -Verbose have no effect.
                   Cannot be combined with -Validate.

              Report:
              [-Report <path>]
                   Write a CSV report in addition to normal console
                   output. Valid in every mode. Source/Encoding/BOM
                   describe the original file; Target/BOM/Result
                   describe the operation performed.

              Performance:
              [-MaxParallelism <N>]
                   Maximum number of files processed concurrently.
                   Default: min(logical processor count, 4).

              Dry run / safety:
              [-WhatIf]
                   Convert mode only. Computes what each file's result
                   would be without writing anything.
              [-Backup]
                   Convert mode only (ignored under -WhatIf). Before
                   overwriting a file, copies the original to
                   "<file>.bak" (overwriting any previous one).

              Output:
              [-Quiet]
                   Suppress per-file CSV rows on stdout; print only a
                   final summary line. -Report is unaffected.
              [-Verbose]
                   Print full error detail for Error rows and a result
                   breakdown, in addition to the CSV rows.

              CI / Exit Code:
              [-FailOnChanges]
                   Exit with a non-zero code when any file requires (or,
                   under -Validate, fails) conversion. Useful as a CI
                   gate, optionally with -WhatIf or -Validate.

        Exit codes: 0 = clean; 1 = usage/argument error (nothing was scanned);
        2 = -FailOnChanges triggered; 3 = the run did not complete cleanly -
        one or more files failed to process, the scan itself failed, or the
        -Report file could not be written; 4 = cancelled (Ctrl+C).

        Examples:

          EncodingChecker.exe -BasePath C:\Source -Include "*.cs,*.txt" -Target "utf-8"

          EncodingChecker.exe -BasePath . -Include "*.cpp,*.hpp" -Target "utf-8" -WhatIf

          EncodingChecker.exe -BasePath . -Include "*.cs" -Exclude "*.designer.cs,*.g.cs" -Target "utf-8-bom"

          EncodingChecker.exe -BasePath . -Include "*" -DetectOnly -Report report.csv

          EncodingChecker.exe -BasePath . -Include "*" -Validate "utf-8,utf-8-bom" -Report report.csv

          EncodingChecker.exe -BasePath . -Include "*.txt" -From "windows-1252" -Target "utf-8"

          EncodingChecker.exe -BasePath . -Include "*" -Target "utf-8" -Plan plan.json
          EncodingChecker.exe -Apply plan.json

          EncodingChecker.exe -BasePath D:\NetworkShare -Include "*.txt" -Target "utf-8" -MaxParallelism 2 -FailOnChanges
        """;

    /// <summary>
    /// Carries out a plan written by -Plan, after confirming it still describes the
    /// files on disk.
    /// </summary>
    private static int ApplyPlan(CliOptions options)
    {
        ConversionPlan? plan = ConversionPlan.Load(options.ApplyPath!, out string? loadError);

        if (plan is null)
        {
            Console.Error.WriteLine($"The plan could not be read: {loadError}");
            return 1;
        }

        // Every path in the plan is relative to this, so if it is gone there is nothing
        // to resolve them against and no way to tell which tree was meant.
        if (!Directory.Exists(plan.BaseDirectory))
        {
            Console.Error.WriteLine(
                $"The plan's directory no longer exists: {plan.BaseDirectory}");
            return 3;
        }

        // The whole reason a plan exists. Re-detecting here would make the preview a
        // demonstration rather than a promise: a second pass can reach different
        // conclusions, and it was the first that the user approved.
        IReadOnlyList<string> stale = plan.FindStaleFiles();

        if (stale.Count > 0)
        {
            Console.Error.WriteLine(
                $"The plan no longer describes these files, so nothing was converted:");

            foreach (string entry in stale.Take(20))
                Console.Error.WriteLine($"  {entry}");

            if (stale.Count > 20)
                Console.Error.WriteLine($"  ...and {stale.Count - 20} more.");

            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "Re-run -Plan to produce a plan for the files as they are now.");
            return 3;
        }

        List<ConversionReportEntry> entries =
        [
            .. plan.Files
                .Where(f => f.Action == PlannedAction.Convert)
                .Select(f => new ConversionReportEntry
                {
                    FilePath = plan.ResolvePath(f)!,
                    SourceEncoding = f.SourceEncoding,
                    SourceHasBom = f.SourceHasBom,
                    TargetEncoding = plan.TargetEncoding,
                    TargetHasBom = plan.TargetHasBom,
                    // The plan already settled this. Re-deriving it would be the second
                    // detection pass the plan exists to avoid; carrying the decision
                    // across is what tells the engine not to classify again.
                    Action = f.Action,
                    Ambiguity = f.Ambiguity,
                    AmbiguityReason = f.AmbiguityReason,
                    CompetingEncodings = f.CompetingEncodings,
                    SourceEncodingWasSpecified = f.SourceWasSpecified,

                    // Checked again at the moment of installation. FindStaleFiles above
                    // proved the file matched when this run started; a long conversion
                    // leaves room for that to stop being true.
                    ExpectedSourceSha256 = f.Sha256,
                })
        ];

        var completed = new List<ConversionReportEntry>();

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
                completed.Add,
                cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 4;
        }

        foreach (ConversionReportEntry entry in completed
                     .Where(e => e.Result == ConversionRowResult.Error))
        {
            Console.Error.WriteLine($"Error: {entry.FilePath}: {entry.Diagnostic}");
        }

        Dictionary<ConversionRowResult, int> byResult =
            completed.GroupBy(e => e.Result).ToDictionary(g => g.Key, g => g.Count());

        int Count(ConversionRowResult result) => byResult.GetValueOrDefault(result);

        int failed = Count(ConversionRowResult.Error);

        // Every planned file is accounted for. A file that the plan scheduled but that
        // the run left alone is the interesting case, so it must not disappear into a
        // difference between two totals.
        Console.Out.WriteLine(
            $"Applied plan: {Count(ConversionRowResult.Converted)} converted, "
            + $"{Count(ConversionRowResult.Unchanged)} already in the target encoding, "
            + $"{Count(ConversionRowResult.Skipped)} skipped, "
            + $"{failed} failed, "
            + $"{plan.Files.Count - entries.Count} not scheduled for conversion.");

        return failed > 0 ? 3 : 0;
    }

    // Internal so ExitCodeContractTests can pin the exit codes, which are a published
    // CLI contract shared with LineEndingNormalizer.
    internal static int RunConsoleMode(string[] args)
    {
        if (args.Length == 1 &&
            args[0] is "/?" or "-?" or "/h" or "-h" or "--help")
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

        // A plan is a dry run that is written down, so it must not modify anything.
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
        };

        // onEntry fires concurrently, so collect into a ConcurrentBag, then sort by
        // path once scanning finishes for deterministic downstream output.
        var collectedEntries = new ConcurrentBag<ConversionReportEntry>();

        using var cancellation = new CancellationTokenSource();

        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            // Let the scan observe cancellation and unwind cleanly instead of the
            // process being torn down mid-write.
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
            // Exit 3, not 1: the arguments were valid and the run started; this is a
            // processing failure, and a CI gate must be able to tell them apart.
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
                // Exit 3, not 1: in Convert mode the files have already been rewritten
                // by this point, so reporting a usage error here would be misleading.
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

            // A refusal is one of the answers a preflight exists to give, so it does not
            // make the preflight itself a failure. Returning 3 here would make the
            // ordinary sequence - plan, read it, apply it - unreachable for exactly the
            // directories this was built for.
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

    private static void PrintVerboseSummary(
        List<ConversionReportEntry> entries)
    {
        foreach (ConversionReportEntry entry in entries)
        {
            if (entry.Result == ConversionRowResult.Error &&
                !string.IsNullOrEmpty(entry.Diagnostic))
            {
                Console.Error.WriteLine(
                    $"Error: {entry.FilePath}: {entry.Diagnostic}");
            }
        }

        var byResult =
            entries
                .GroupBy(e => e.Result)
                .ToDictionary(g => g.Key, g => g.Count());

        Console.Out.WriteLine();
        Console.Out.WriteLine(
            $"Total: {entries.Count}  " +
            $"Unchanged: {byResult.GetValueOrDefault(ConversionRowResult.Unchanged)}  " +
            $"Skipped: {byResult.GetValueOrDefault(ConversionRowResult.Skipped)}  " +
            $"Converted: {byResult.GetValueOrDefault(ConversionRowResult.Converted)}  " +
            $"Invalid: {byResult.GetValueOrDefault(ConversionRowResult.Invalid)}  " +
            $"Error: {byResult.GetValueOrDefault(ConversionRowResult.Error)}");
    }

    // internal so EncodingChecker.Tests can cover parsing directly (see CliOptions).
    internal static bool TryParseArguments(
        string[] args,
        out CliOptions options,
        [NotNullWhen(false)] out string? error)
    {
        options = new CliOptions();
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            string flag = args[i].TrimStart('-');

            switch (flag.ToLowerInvariant())
            {
                case "basepath":
                    if (!TryTakeValue(
                            args,
                            ref i,
                            out options.BasePath))
                    {
                        error = "-BasePath requires a value.";
                        return false;
                    }
                    break;

                case "include":
                    if (!TryTakeValue(
                            args,
                            ref i,
                            out string? include))
                    {
                        error = "-Include requires a value.";
                        return false;
                    }
                    // Accumulate: repeating the option used to discard the earlier
                    // patterns silently, so only the last one took effect.
                    options.Include.AddRange(SplitCommaList(include));
                    break;

                case "exclude":
                    if (!TryTakeValue(
                            args,
                            ref i,
                            out string? exclude))
                    {
                        error = "-Exclude requires a value.";
                        return false;
                    }
                    // Accumulates for the same reason as -Include, keeping the two
                    // options consistent with each other.
                    options.Exclude.AddRange(SplitCommaList(exclude));
                    break;

                case "target":
                    if (!TryTakeValue(
                            args,
                            ref i,
                            out options.Target))
                    {
                        error = "-Target requires a value.";
                        return false;
                    }
                    break;

                case "from":
                    if (!TryTakeValue(
                            args,
                            ref i,
                            out options.From))
                    {
                        error = "-From requires a value.";
                        return false;
                    }
                    break;

                case "plan":
                    if (!TryTakeValue(args, ref i, out options.PlanPath))
                    {
                        error = "-Plan requires a value.";
                        return false;
                    }
                    break;

                case "apply":
                    if (!TryTakeValue(args, ref i, out options.ApplyPath))
                    {
                        error = "-Apply requires a value.";
                        return false;
                    }
                    break;

                case "validate":
                    if (!TryTakeValue(
                            args,
                            ref i,
                            out options.ValidateCharsets))
                    {
                        error = "-Validate requires a value.";
                        return false;
                    }
                    break;

                case "detectonly":
                    options.DetectOnly = true;
                    break;

                case "report":
                    if (!TryTakeValue(
                            args,
                            ref i,
                            out options.ReportPath))
                    {
                        error = "-Report requires a value.";
                        return false;
                    }
                    break;

                case "maxparallelism":
                    if (!TryTakeValue(
                            args,
                            ref i,
                            out string? maxParallelismText) ||
                        !int.TryParse(
                            maxParallelismText,
                            out int maxParallelism) ||
                        maxParallelism <= 0)
                    {
                        error =
                            "-MaxParallelism requires a positive integer.";
                        return false;
                    }

                    options.MaxParallelism = maxParallelism;
                    break;

                case "failonchanges":
                    options.FailOnChanges = true;
                    break;

                case "whatif":
                    options.WhatIf = true;
                    break;

                case "backup":
                    options.Backup = true;
                    break;

                case "quiet":
                    options.Quiet = true;
                    break;

                case "verbose":
                    options.Verbose = true;
                    break;

                default:
                    error = $"Unrecognized argument: {args[i]}";
                    return false;
            }
        }

        return true;
    }

    // Lets TryTakeValue detect "-BasePath -Include" as a missing value, not a path.
    private static readonly HashSet<string> KnownFlagNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "basepath", "include", "exclude", "target", "from", "plan", "apply",
            "validate",
            "detectonly", "report", "maxparallelism", "failonchanges",
            "whatif", "backup", "quiet", "verbose",
        };

    // internal so EncodingChecker.Tests can cover parsing directly (see CliOptions).
    internal static bool TryTakeValue(
        string[] args,
        ref int i,
        [NotNullWhen(true)] out string? value)
    {
        if (i + 1 >= args.Length)
        {
            value = null;
            return false;
        }

        string candidate = args[i + 1];

        if (candidate.StartsWith('-') &&
            KnownFlagNames.Contains(candidate.TrimStart('-')))
        {
            value = null;
            return false;
        }

        value = args[++i];
        return true;
    }

    private static List<string> SplitCommaList(string value) =>
    [
        .. value.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries)
    ];

    // internal so EncodingChecker.Tests can cover validation directly (see CliOptions).
    internal static bool TryValidateOptions(
        CliOptions options,
        [NotNullWhen(false)] out string? error)
    {
        if (!string.IsNullOrWhiteSpace(options.PlanPath) &&
            !string.IsNullOrWhiteSpace(options.ApplyPath))
        {
            error = "-Plan writes a plan and -Apply executes one; use them in "
                    + "separate runs so the plan can be reviewed in between.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.ApplyPath))
        {
            if (!File.Exists(options.ApplyPath))
            {
                error = $"The plan file '{options.ApplyPath}' does not exist.";
                return false;
            }

            if (options.DetectOnly || !string.IsNullOrWhiteSpace(options.ValidateCharsets))
            {
                error = "-Apply performs a conversion; it cannot be combined with "
                        + "-DetectOnly or -Validate.";
                return false;
            }

            // The plan already fixes each of these, so accepting them here would let a
            // user write a flag that reads as an instruction and is silently ignored -
            // -Backup being the one that matters, since it would appear to ask for
            // originals to be kept while the plan says otherwise.
            string? overridden =
                options.BasePath != null ? "-BasePath"
                : options.Target != null ? "-Target"
                : options.From != null ? "-From"
                : options.Backup ? "-Backup"
                : null;

            if (overridden != null)
            {
                error = $"{overridden} has no effect with -Apply: a plan already records "
                        + "the files, the source and target encodings, and whether "
                        + "originals are backed up. Re-run -Plan to change any of them.";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(options.PlanPath) &&
            (options.DetectOnly || !string.IsNullOrWhiteSpace(options.ValidateCharsets)))
        {
            error = "-Plan previews a conversion; it cannot be combined with "
                    + "-DetectOnly or -Validate.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.From))
        {
            if (options.DetectOnly || !string.IsNullOrWhiteSpace(options.ValidateCharsets))
            {
                error = "-From applies to conversion only; it cannot be combined with "
                        + "-DetectOnly or -Validate, which report what the detector finds.";
                return false;
            }

            try
            {
                Encoding.GetEncoding(options.From!);
            }
            catch (ArgumentException)
            {
                error = $"'{options.From}' is not a recognized encoding.";
                return false;
            }
        }

        // A plan already names every file, so -Apply supplies its own scope.
        if (!string.IsNullOrWhiteSpace(options.ApplyPath))
        {
            error = null;
            return true;
        }

        if (string.IsNullOrWhiteSpace(options.BasePath))
        {
            error = "-BasePath is required.";
            return false;
        }

        if (!Directory.Exists(options.BasePath))
        {
            error =
                $"The directory '{options.BasePath}' does not exist.";
            return false;
        }

        if (DirectoryTraversal.IsReparsePointDirectory(options.BasePath))
        {
            error =
                $"'{options.BasePath}' is a symbolic link or other reparse point; " +
                "-BasePath must be a real directory.";
            return false;
        }

        if (options is
            {
                DetectOnly: true,
                ValidateCharsets: not null
            })
        {
            error =
                "-DetectOnly cannot be combined with -Validate.";
            return false;
        }

        // Validate and Convert are separate modes. Accepting both silently ran Validate
        // and ignored -Target, so a caller expecting files to be converted was told
        // nothing while nothing was converted.
        if (options is
            {
                ValidateCharsets: not null,
                Target: not null
            })
        {
            error =
                "-Validate cannot be combined with -Target.";
            return false;
        }

        if (options.ValidateCharsets is not null &&
            SplitCommaList(options.ValidateCharsets).Count == 0)
        {
            error = "-Validate requires at least one charset.";
            return false;
        }

        bool isConvertMode =
            options is
            {
                DetectOnly: false,
                ValidateCharsets: null
            };

        if (isConvertMode &&
            string.IsNullOrWhiteSpace(options.Target))
        {
            error =
                "-Target is required (Convert is the default mode; " +
                "use -Validate or -DetectOnly for read-only modes).";
            return false;
        }

        if (isConvertMode &&
            !string.IsNullOrWhiteSpace(options.Target))
        {
            ScanEngine.ParseCharsetLabel(
                options.Target!,
                out string baseCharset,
                out _);

            try
            {
                Encoding.GetEncoding(baseCharset);
            }
            catch (ArgumentException)
            {
                error =
                    $"'{options.Target}' is not a recognized encoding.";
                return false;
            }
        }

        error = null;
        return true;
    }

    #endregion
}
