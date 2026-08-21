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

namespace EncodingChecker
{
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
            EncodingChecker v3.0

            Usage:

              EncodingChecker.exe
                  -BasePath <directory>
                  [-Include "<pattern1,pattern2,...>"]
                  [-Exclude "<pattern1,pattern2,...>"]
                       Wildcard file-name patterns. Exclusions are applied
                       after -Include. The following directories are always
                       skipped: .git, .svn, .hg, .vs, .idea, bin, obj,
                       node_modules, packages, dist, build, target.

                  Conversion:
                  [-Target "<encoding>"]
                       The name of the encoding to convert files to, e.g.
                       "utf-8" or "utf-8-bom". Required unless -Validate or
                       -DetectOnly is given.

                  Modes:
                       Conversion is the default mode.
                  [-Validate "<charset1,charset2,...>"]
                       Validate without writing anything. Files whose current
                       encoding isn't in this list are reported as Invalid.
                       May be combined with -FailOnChanges. Cannot be
                       combined with -DetectOnly.
                  [-DetectOnly]
                       Read-only detection mode. Writes File;Encoding;BOM;
                       Target;BOM;Result CSV rows to stdout (Target/Result
                       always mirror the source, since nothing changes).
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

            Exit codes: 0 = clean; 1 = usage/argument error; 2 = -FailOnChanges
            triggered; 3 = one or more files failed to process; 4 = cancelled
            (Ctrl+C).

            Examples:

              EncodingChecker.exe -BasePath C:\Source -Include "*.cs,*.txt" -Target "utf-8"

              EncodingChecker.exe -BasePath . -Include "*.cpp,*.hpp" -Target "utf-8" -WhatIf

              EncodingChecker.exe -BasePath . -Include "*.cs" -Exclude "*.designer.cs,*.g.cs" -Target "utf-8-bom"

              EncodingChecker.exe -BasePath . -Include "*" -DetectOnly -Report report.csv

              EncodingChecker.exe -BasePath . -Include "*" -Validate "utf-8,utf-8-bom" -Report report.csv

              EncodingChecker.exe -BasePath D:\NetworkShare -Include "*.txt" -Target "utf-8" -MaxParallelism 2 -FailOnChanges
            """;

        private static int RunConsoleMode(string[] args)
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
                BaseDirectory = options.BasePath!,
                IncludeSubdirectories = true,
                IncludePatterns = options.Include,
                ExcludePatterns = options.Exclude,
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
                    cancellation.Token);
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
                return 1;
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
                    Console.Error.WriteLine(
                        $"Failed to write report file: {ex.Message}");
                    return 1;
                }
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
                        options.Include = SplitCommaList(include);
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
                        options.Exclude = SplitCommaList(exclude);
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
                "basepath", "include", "exclude", "target", "validate",
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
}
