using System.Text;
using System.Text.Json;

namespace EncodingChecker.Tests;

/// <summary>Cancel through the CLI's real completion callback, not a simulated result list.</summary>
public sealed class CliInterruptedReportingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ec_cli_cancel_").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string PathFor(string name) => Path.Combine(_root, name);

    private void CreateInputs(string text = "plain text")
    {
        for (int i = 0; i < 6; i++)
            File.WriteAllText(PathFor($"{i}.txt"), text, new UTF8Encoding(true));
    }

    private static (int Exit, string Output, string Error) Run(
        string[] args, CancellationToken token = default,
        Action<ConversionReportEntry>? completed = null)
    {
        TextWriter originalOut = Console.Out, originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            int exit = Program.RunConsoleMode(args, token, completed);
            return (exit, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 3)]
    public void InterruptedScanWritesPartialEvidenceAndPreservesErrorPrecedence(bool fail, int expected)
    {
        CreateInputs(fail ? "café" : "plain text");
        var before = Directory.GetFiles(_root, "*.txt").ToDictionary(p => p, File.ReadAllBytes);
        string csv = PathFor("report.csv"), journal = PathFor("journal.json");
        File.WriteAllText(csv, "old report");
        using var cancellation = new CancellationTokenSource();
        var reached = new EntrySink();
        var result = Run(
            ["-BasePath", _root, "-Include", "*.txt", "-Target", fail ? "us-ascii" : "utf-8",
             "-MaxParallelism", "1", "-Report", csv, "-Journal", journal],
            cancellation.Token, entry => { reached.Add(entry); cancellation.Cancel(); });

        Assert.Equal(expected, result.Exit);
        Assert.InRange(reached.Count(), 1, 5);
        Assert.DoesNotContain("old report", File.ReadAllText(csv));
        using var document = JsonDocument.Parse(File.ReadAllText(journal));
        Assert.True(document.RootElement.GetProperty("Interrupted").GetBoolean());
        Assert.Equal(reached.Count(), document.RootElement.GetProperty("Entries").GetArrayLength());
        Assert.Equal(reached.Count() + 1, File.ReadAllLines(csv).Length);
        var changed = reached.Where(e => e.Result == ConversionRowResult.Converted)
            .Select(e => e.FilePath).ToHashSet();
        foreach (var pair in before)
            if (!changed.Contains(pair.Key)) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
        Assert.All(reached, e => Assert.Equal(
            fail ? ConversionRowResult.Error : ConversionRowResult.Converted, e.Result));
    }

    [Fact]
    public void InterruptedApplyReconcilesConsoleJournalAndUntouchedBytes()
    {
        CreateInputs();
        var before = Directory.GetFiles(_root, "*.txt").ToDictionary(p => p, File.ReadAllBytes);
        string plan = PathFor("plan.json"), journal = PathFor("journal.json");
        Assert.Equal(0, Run(["-BasePath", _root, "-Include", "*.txt", "-Target", "utf-8", "-Plan", plan]).Exit);
        using var cancellation = new CancellationTokenSource();
        var reached = new EntrySink();
        var result = Run(["-Apply", plan, "-Journal", journal, "-MaxParallelism", "1"],
            cancellation.Token, entry => { reached.Add(entry); cancellation.Cancel(); });

        Assert.Equal(4, result.Exit);
        int written = reached.Count();
        Assert.InRange(written, 1, 5);
        Assert.Contains($"{written} converted, 0 unchanged", result.Output);
        Assert.Contains($"{6 - written} not attempted", result.Output);
        using var document = JsonDocument.Parse(File.ReadAllText(journal));
        var root = document.RootElement;
        Assert.True(root.GetProperty("Interrupted").GetBoolean());
        Assert.Equal(6, root.GetProperty("Entries").GetArrayLength());
        Assert.Equal(6 - written, root.GetProperty("Summary").GetProperty("NotAttempted").GetInt32());
        foreach (var item in root.GetProperty("Entries").EnumerateArray())
        {
            string path = PathFor(item.GetProperty("RelativePath").GetString()!);
            if (item.GetProperty("Status").GetString() == "NotAttempted")
                Assert.Equal(before[path], File.ReadAllBytes(path));
            else
                Assert.Equal("plain text", new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path)));
        }
    }

    [Fact]
    public void CancelledPreflightDoesNotReplaceAnExistingPlan()
    {
        CreateInputs();
        string plan = PathFor("plan.json"), csv = PathFor("report.csv");
        File.WriteAllText(plan, "previous approved plan");
        using var cancellation = new CancellationTokenSource();
        var result = Run(["-BasePath", _root, "-Include", "*.txt", "-Target", "utf-8",
            "-Plan", plan, "-Report", csv, "-MaxParallelism", "1"],
            cancellation.Token, _ => cancellation.Cancel());
        Assert.Equal(4, result.Exit);
        Assert.Equal("previous approved plan", File.ReadAllText(plan));
        Assert.Contains("No new plan was written", result.Error);
        Assert.True(File.Exists(csv));
        Assert.All(Directory.GetFiles(_root, "*.txt"), path =>
            Assert.True(File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.GetPreamble())));
    }
}
