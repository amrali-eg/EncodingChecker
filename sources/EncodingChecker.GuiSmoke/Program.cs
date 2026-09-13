using System.IO;
using System.Text;

namespace EncodingChecker.GuiSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            if (args.Any(arg => arg is "-h" or "--help"))
            {
                Console.WriteLine(Options.Usage);
                return 0;
            }

            Options options;
            try
            {
                options = Options.Parse(args);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(Options.Usage);
                return 2;
            }

            try
            {
                PrepareOutputDirectory(options.Output);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            string workspace = Path.Combine(options.Output, "workspace");
            Directory.CreateDirectory(workspace);

            if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            {
                return WriteStartupInconclusive(
                    options,
                    workspace,
                    "The GUI smoke test requires an interactive Windows desktop.");
            }

            if (!File.Exists(options.App))
            {
                return WriteStartupInconclusive(
                    options,
                    workspace,
                    $"EncodingChecker was not found: {options.App}");
            }

            var suite = new SmokeSuite(options.App, workspace);
            SmokeReport report = suite.Run(options.Phase);

            if (report.Passed && !options.KeepWorkspace)
            {
                try
                {
                    Directory.Delete(workspace, recursive: true);
                }
                catch (Exception ex)
                {
                    report = report with
                    {
                        Outcome = SmokeOutcome.Failed,
                        Error = "The test workspace could not be removed: " + ex,
                    };
                }
            }

            SmokeReportWriter.Write(options.Output, report);

            Console.WriteLine();
            Console.WriteLine($"GUI SMOKE TEST: {report.Outcome.Label()}");
            Console.WriteLine($"Evidence: {options.Output}");
            return report.Outcome.ExitCode();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void PrepareOutputDirectory(string output)
    {
        if (Directory.Exists(output) &&
            Directory.EnumerateFileSystemEntries(output).Any())
        {
            throw new ArgumentException(
                $"The output folder must be empty or new: {output}");
        }

        Directory.CreateDirectory(output);
    }

    private static int WriteStartupInconclusive(Options options, string workspace, string error)
    {
        string now = DateTime.UtcNow.ToString("O");
        SmokeBuildEvidence build = SmokeBuildEvidence.Capture(options.App);
        var report = new SmokeReport
        {
            StartedUtc = now,
            CompletedUtc = now,
            EcExecutable = options.App,
            EcVersion = build.Version,
            EcSha256 = build.ExecutableSha256,
            EcManagedAssembly = build.ManagedAssembly,
            EcManagedAssemblySha256 = build.ManagedAssemblySha256,
            EvidenceErrors = build.Errors,
            OS = Environment.OSVersion.VersionString,
            DotNet = Environment.Version.ToString(),
            Workspace = workspace,
            DurationMilliseconds = 0,
            Outcome = SmokeOutcome.Inconclusive,
            Error = error,
            Preflight = new SmokePhaseResult
            {
                Id = "preflight",
                Name = "Check the GUI smoke test can start",
                Outcome = SmokeOutcome.Inconclusive,
                Error = error,
                DurationMilliseconds = 0,
                Before = new Dictionary<string, string>(),
                After = new Dictionary<string, string>(),
            },
            Phases = Array.Empty<SmokePhaseResult>(),
        };

        try
        {
            SmokeReportWriter.Write(options.Output, report);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("The startup refusal could not be recorded: " + ex);
            return 1;
        }

        Console.Error.WriteLine(error);
        Console.WriteLine();
        Console.WriteLine($"GUI SMOKE TEST: {report.Outcome.Label()}");
        Console.WriteLine($"Evidence: {options.Output}");
        return report.Outcome.ExitCode();
    }

    private sealed record Options(
        string App,
        string Output,
        bool KeepWorkspace,
        string? Phase)
    {
        internal const string Usage =
            "Usage: EncodingChecker.GuiSmoke [--app <EncodingChecker.exe>] "
            + "[--output <folder>] [--phase <A-J>] [--keep-workspace]";

        internal static Options Parse(string[] args)
        {
            string app = DefaultAppPath();
            string output = Path.Combine(
                Path.GetTempPath(),
                "EncodingChecker-GuiSmoke",
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            bool keep = false;
            string? phase = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--app":
                        app = TakeValue(args, ref i, "--app");
                        break;
                    case "--output":
                        output = TakeValue(args, ref i, "--output");
                        break;
                    case "--keep-workspace":
                        keep = true;
                        break;
                    case "--phase":
                        phase = TakeValue(args, ref i, "--phase").ToUpperInvariant();

                        if (phase is not ("A" or "B" or "C" or "D" or "E" or "F" or "G"
                                          or "H" or "I" or "J"))
                            throw new ArgumentException("--phase must be one letter from A to J.");
                        break;
                    default:
                        throw new ArgumentException($"Unknown option: {args[i]}");
                }
            }

            return new Options(
                Path.GetFullPath(app),
                Path.GetFullPath(output),
                keep,
                phase);
        }

        private static string TakeValue(string[] args, ref int index, string option)
        {
            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                throw new ArgumentException($"{option} requires a value.");

            return args[index];
        }

        private static string DefaultAppPath() =>
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "EncodingChecker", "bin", "Release", "net10.0-windows",
                "EncodingChecker.exe"));
    }
}
