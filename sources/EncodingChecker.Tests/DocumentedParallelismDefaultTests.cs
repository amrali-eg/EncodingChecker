using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace EncodingChecker.Tests;

/// <summary>
/// The documented default parallelism must be the one the code actually uses.
/// </summary>
/// <remarks>
/// <see cref="ScanEngine.DefaultMaxParallelism"/> was raised from 4 to 8 with a measurement
/// recorded beside it, and both statements of it — the built-in help and
/// <c>docs/CLI.md</c> — were left saying 4. Nothing failed, which is the problem: someone
/// tuning <c>-MaxParallelism</c> against a slow share was being given the wrong baseline
/// by the two places they would look.
/// </remarks>
public sealed class DocumentedParallelismDefaultTests
{
    private static string RepositoryRoot([CallerFilePath] string thisFile = "")
    {
        string? directory = Path.GetDirectoryName(thisFile);

        while (directory is not null &&
               !File.Exists(Path.Combine(directory, "docs", "CLI.md")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        Assert.True(
            directory is not null,
            "docs/CLI.md was not found above the test sources; this check reads it, so it "
            + "has to run from a source checkout");

        return directory!;
    }

    /// <summary>The one line in a document that states the option's default.</summary>
    private static string TheLineStatingTheDefault(string text, string source)
    {
        string[] candidates =
        [
            .. text.Split('\n')
                   .Select(line => line.TrimEnd('\r'))
                   .Where(line =>
                       line.Contains("-MaxParallelism", StringComparison.Ordinal) &&
                       line.Contains("efault", StringComparison.Ordinal))
        ];

        Assert.True(
            candidates.Length == 1,
            $"expected exactly one line in {source} stating the -MaxParallelism default, "
            + $"found {candidates.Length}; the check is not looking where it thinks it is");

        return candidates[0];
    }

    /// <summary>Every run of digits in the line, so a stale number cannot hide beside a fresh one.</summary>
    private static string[] NumbersIn(string line) =>
        [.. Regex.Matches(line, @"\d+").Select(m => m.Value)];

    private static string HelpText()
    {
        TextWriter originalOut = Console.Out;

        try
        {
            using var captured = new StringWriter();
            Console.SetOut(captured);
            Assert.Equal(0, Program.RunConsoleMode(["--help"]));
            return captured.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void TheBuiltInHelpStatesTheCapTheCodeUses()
    {
        string line = TheLineStatingTheDefault(HelpText(), "the built-in help");

        Assert.Equal([ScanEngine.MaxParallelismCap.ToString()], NumbersIn(line));
    }

    [Fact]
    public void TheCommandLineReferenceStatesTheCapTheCodeUses()
    {
        string reference = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "docs", "CLI.md"));

        string line = TheLineStatingTheDefault(reference, "docs/CLI.md");

        Assert.Equal([ScanEngine.MaxParallelismCap.ToString()], NumbersIn(line));
    }

    [Fact]
    public void TheCapIsWhatBoundsTheDefault()
    {
        // Ties the documented number to the value actually handed to Parallel.ForEach,
        // so documenting the cap correctly cannot drift from applying it.
        Assert.Equal(
            Math.Min(Environment.ProcessorCount, ScanEngine.MaxParallelismCap),
            ScanEngine.DefaultMaxParallelism);
    }
}
