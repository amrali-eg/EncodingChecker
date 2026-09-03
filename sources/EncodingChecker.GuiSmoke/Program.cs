using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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

            Options options = Options.Parse(args);

            if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            {
                Console.Error.WriteLine(
                    "The GUI smoke test requires an interactive Windows desktop.");
                return 2;
            }

            if (!File.Exists(options.App))
            {
                Console.Error.WriteLine($"EncodingChecker was not found: {options.App}");
                return 2;
            }

            PrepareOutputDirectory(options.Output);
            string workspace = Path.Combine(options.Output, "workspace");
            Directory.CreateDirectory(workspace);

            var suite = new SmokeSuite(options.App, workspace);
            SmokeReport report = suite.Run(options.Phase) with
            {
                EcVersion = FileVersionInfo.GetVersionInfo(options.App).FileVersion
                            ?? "unknown",
                EcManagedAssembly = ManagedAssemblyPath(options.App),
                EcManagedAssemblySha256 = HashIfPresent(ManagedAssemblyPath(options.App)),
            };

            WriteReports(options.Output, report);

            if (report.Passed && !options.KeepWorkspace)
                Directory.Delete(workspace, recursive: true);

            Console.WriteLine();
            Console.WriteLine(report.Passed
                ? "GUI SMOKE TEST: PASS"
                : "GUI SMOKE TEST: FAIL");
            Console.WriteLine($"Evidence: {options.Output}");
            return report.Passed ? 0 : 1;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Options.Usage);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
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

    private static string ManagedAssemblyPath(string app) =>
        Path.ChangeExtension(app, ".dll");

    private static string? HashIfPresent(string path)
    {
        if (!File.Exists(path))
            return null;

        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void WriteReports(string output, SmokeReport report)
    {
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(
            Path.Combine(output, "gui-smoke-report.json"),
            JsonSerializer.Serialize(report, jsonOptions),
            new UTF8Encoding(false));

        var markdown = new StringBuilder()
            .AppendLine("# EncodingChecker automated GUI smoke test")
            .AppendLine()
            .AppendLine($"- Result: **{(report.Passed ? "PASS" : "FAIL")}**")
            .AppendLine($"- EC version: `{report.EcVersion}`")
            .AppendLine($"- Executable SHA-256: `{report.EcSha256}`")
            .AppendLine($"- Managed assembly: `{report.EcManagedAssembly}`")
            .AppendLine($"- Managed assembly SHA-256: `{report.EcManagedAssemblySha256}`")
            .AppendLine($"- Started UTC: `{report.StartedUtc}`")
            .AppendLine($"- Completed UTC: `{report.CompletedUtc}`")
            .AppendLine($"- Windows: `{report.OS}`")
            .AppendLine($"- .NET: `{report.DotNet}`")
            .AppendLine()
            .AppendLine("| Phase | Result | Check |")
            .AppendLine("|---|---|---|");

        foreach (SmokePhaseResult phase in report.Phases)
        {
            markdown.AppendLine(
                $"| {phase.Id} | {(phase.Passed ? "PASS" : "FAIL")} | "
                + $"{EscapeCell(phase.Name)} |");
        }

        SmokePhaseResult[] failures = [.. report.Phases.Where(phase => !phase.Passed)];

        if (failures.Length > 0)
        {
            markdown.AppendLine().AppendLine("## Failures");

            foreach (SmokePhaseResult phase in failures)
            {
                markdown.AppendLine().AppendLine($"### Phase {phase.Id}")
                    .AppendLine().AppendLine("```text")
                    .AppendLine(phase.Error)
                    .AppendLine("```");
            }
        }

        File.WriteAllText(
            Path.Combine(output, "gui-smoke-report.md"),
            markdown.ToString(),
            new UTF8Encoding(false));
    }

    private static string EscapeCell(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
             .Replace("\r", " ", StringComparison.Ordinal)
             .Replace("\n", " ", StringComparison.Ordinal);

    private sealed record Options(
        string App,
        string Output,
        bool KeepWorkspace,
        string? Phase)
    {
        internal const string Usage =
            "Usage: EncodingChecker.GuiSmoke [--app <EncodingChecker.exe>] "
            + "[--output <folder>] [--phase <A-I>] [--keep-workspace]";

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
                                          or "H" or "I"))
                            throw new ArgumentException("--phase must be one letter from A to I.");
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
