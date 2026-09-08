using System.Text.Json;

namespace EncodingChecker.Tests;

/// <summary>
/// A plan and a journal say what their semantics version guarantees, and claim nothing
/// they do not check.
/// </summary>
/// <remarks>
/// EC-15. A guarantee flag that no build can vary is not evidence a check ran, so the
/// artifacts name their semantics version and describe it, and claim nothing else.
/// </remarks>
public sealed class SemanticsArtifactShapeTests : IDisposable
{
    private static readonly string[] RemovedFlags =
    [
        "StrictDecoding",
        "StrictEncoding",
        "OutputVerification",
        "AtomicInstall",
        "LegacyRequiresExplicitSource",
    ];

    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_semantics_").FullName;

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

    private static int Run(params string[] args)
    {
        TextWriter outWriter = Console.Out;
        TextWriter errWriter = Console.Error;

        try
        {
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());

            return Program.RunConsoleMode(args);
        }
        finally
        {
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
        }
    }

    private JsonDocument WriteAndRead(bool journal)
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "hello\n");
        string path = Path.Combine(_root, journal ? "journal.json" : "plan.json");

        Assert.Equal(0, journal
            ? Run("-BasePath", _root, "-Target", "utf-16-bom", "-Journal", path, "-Quiet")
            : Run("-BasePath", _root, "-Target", "utf-16-bom", "-Plan", path, "-Quiet"));

        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void APlanStatesWhatItsSemanticsVersionGuarantees()
    {
        using JsonDocument document = WriteAndRead(journal: false);
        JsonElement root = document.RootElement;

        Assert.Equal(
            ConversionSemantics.Describes,
            root.GetProperty("SemanticsDescription").GetString());

        Assert.Equal(ConversionSemantics.Current, root.GetProperty("SemanticsVersion").GetInt32());
    }

    [Fact]
    public void AJournalStatesWhatItsSemanticsVersionGuarantees()
    {
        using JsonDocument document = WriteAndRead(journal: true);
        JsonElement root = document.RootElement;

        Assert.Equal(
            ConversionSemantics.Describes,
            root.GetProperty("SemanticsDescription").GetString());

        Assert.Equal(ConversionSemantics.Current, root.GetProperty("SemanticsVersion").GetInt32());
    }

    /// <summary>
    /// The constants are gone from both artifacts, including the object that held them.
    /// </summary>
    [Fact]
    public void NeitherArtifactCarriesTheConstantGuaranteeFlags()
    {
        foreach (bool journal in new[] { false, true })
        {
            using JsonDocument document = WriteAndRead(journal);
            string json = document.RootElement.GetRawText();

            Assert.False(
                document.RootElement.TryGetProperty("Semantics", out _),
                $"the {(journal ? "journal" : "plan")} still carries a Semantics object");

            foreach (string flag in RemovedFlags)
            {
                Assert.DoesNotContain(flag, json, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>Nothing may consult the description to decide anything.</summary>
    [Fact]
    public void TheDescriptionIsNotUsedToDecideCompatibility()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "hello\n");
        string planPath = Path.Combine(_root, "plan.json");

        Assert.Equal(0, Run(
            "-BasePath", _root, "-Target", "utf-16-bom", "-Plan", planPath, "-Quiet"));

        string text = File.ReadAllText(planPath);
        File.WriteAllText(
            planPath,
            text.Replace(ConversionSemantics.Describes, "something else entirely"));

        ConversionPlan? plan = ConversionPlan.Load(planPath, out string? error);

        Assert.NotNull(plan);
        Assert.Null(error);
    }
}
