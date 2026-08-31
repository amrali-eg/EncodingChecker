using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace EncodingChecker;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Enable legacy code pages required by detection and conversion.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (args.Length > 0)
        {
            bool attachedConsole = TryAttachToParentConsole();

            // Pad only an interactive console so output does not overwrite the shell prompt.
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

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);

    /// <summary>
    /// Attaches to the parent console for CLI launches.
    /// </summary>
    /// <returns><see langword="true"/> if a parent console was attached.</returns>
    private static bool TryAttachToParentConsole()
    {
        if (!AttachConsole(AttachParentProcess))
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

        // A pattern list that parses to nothing is not the same as no list at all:
        // an empty -Include would silently widen the scan to every file. Record
        // that the option was supplied so validation can reject the degenerate form.
        internal bool IncludeSpecified;
        internal bool ExcludeSpecified;

        internal string? Target;
        internal string? From;
        internal string? PlanPath;
        internal string? ApplyPath;
        internal string? JournalPath;
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
        EncodingChecker v3.9.2

        Common commands:

          Preview a folder safely (recommended first use):
            EncodingChecker.exe -BasePath "C:\Files" -Target utf-8 -Plan plan.json

          Review that plan, then apply exactly it:
            EncodingChecker.exe -Apply plan.json

          Convert files whose original legacy encoding you know:
            EncodingChecker.exe -BasePath "C:\Files" -Target utf-8 -From windows-1252 -Backup

          Inspect files only; do not modify them:
            EncodingChecker.exe -BasePath "C:\Files" -DetectOnly

        Automatic conversion rule:

          EC converts Unicode and ASCII automatically. For legacy text, specify its
          original encoding with -From. This replaces detection only; strict decoding,
          output verification, backups, and atomic installation still apply.

        Basic conversion:

          -BasePath <directory>        Folder to scan. Required except with -Apply.
          -Target <encoding>            Target encoding, for example utf-8 or utf-8-bom.
                                      Required for conversion.
          -From <encoding>              Explicit original encoding for every selected file.
                                      Use for legacy text. Convert mode only.
          -Backup                       Save each replaced original as <file>.bak.
          -WhatIf                       Show a one-time preview without writing files.

        Safest batch workflow:

          -Plan <path>                  Write a reviewable conversion plan; change nothing.
                                      It records the selected files, their hashes, source
                                      choices, target/BOM policy, backup setting, and action.
          -Apply <path>                 Execute a saved plan. It needs no other conversion
                                      options. If any scheduled file changed, nothing runs.
                                      Only -Journal, -Quiet, and -MaxParallelism may be
                                      added; -WhatIf and conversion options are rejected.

        Read-only modes:

          -DetectOnly                   Report detected encodings; change nothing.
          -Validate <charset1,...>      Report files not in the allowed encoding list.
                                      Cannot be combined with -DetectOnly or -Target.

        File selection:

          -Include <patterns>           Comma-separated wildcards; may be repeated.
          -Exclude <patterns>           Comma-separated wildcards; may be repeated.
                                      A pattern without / or \ matches filenames at any depth.
                                      A pattern with a separator matches relative paths.

        Output and advanced options:

          -Report <path>                Also write the UTF-8-with-BOM CSV report to a file.
          -Journal <path>               Write a JSON record of the conversion decision and
                                      result for every file. Convert mode only.
          -Quiet                        Print only the final summary on standard output.
          -Verbose                      Include error details and a result breakdown.
          -MaxParallelism <N>           Maximum simultaneous files; default is min(CPU count, 4).
          -FailOnChanges                Return exit code 2 if files need conversion (or fail
                                      validation). Useful for CI.

        Examples:

          Preview selected files without writing anything:
            EncodingChecker.exe -BasePath "C:\Files" -Include "*.cs,*.txt" -Exclude "*.g.cs,*.designer.cs" -Target utf-8 -WhatIf

          Validate a folder in CI and save a quiet CSV report:
            EncodingChecker.exe -BasePath "C:\Files" -Validate "utf-8,utf-8-bom" -Report validation.csv -FailOnChanges -Quiet

          Convert known legacy text, preserving originals and recording the run:
            EncodingChecker.exe -BasePath "C:\Files" -Include "*.txt" -From windows-1252 -Target utf-8 -Backup -Journal conversion.json -Verbose

          Limit work against a network or slow disk:
            EncodingChecker.exe -BasePath "D:\Share" -Target utf-8 -MaxParallelism 2

        Directories named .git, .svn, .hg, .vs, .idea, bin, obj, node_modules,
        packages, dist, build, and target are always skipped. Matching hidden,
        system, and reparse-point files are not examined. Hidden, system, and
        reparse-point folders are not entered. Both counts are reported on stderr
        and are informational; they do not change the exit code.

        Help: -?, /?, -h, /h, or --help.

        Exit codes: 0 = completed; 1 = invalid command; 2 = -FailOnChanges;
        3 = processing, plan, or report failure; 4 = cancelled (Ctrl+C);
        5 = conversion safely refused.
        """;

    #endregion
}
