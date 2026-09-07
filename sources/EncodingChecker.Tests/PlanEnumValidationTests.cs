using System.Text;
using System.Text.Json.Nodes;

namespace EncodingChecker.Tests;

/// <summary>
/// A plan carrying an action no build ever wrote must be refused, not reported as done.
/// </summary>
/// <remarks>
/// JSON accepts undefined numeric enum values. The former fallback mapped one to
/// <see cref="ConversionRowResult.Converted"/>, producing a false success and journal
/// record. Plan validation and the mapping's throw now guard both boundaries.
/// </remarks>
public sealed class PlanEnumValidationTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_planenum_").FullName;

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
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;

        try
        {
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());

            return Program.RunConsoleMode(args);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    /// <summary>Writes a real plan, then replaces one field with an undefined value.</summary>
    private string PlanWithField(string field, object value)
    {
        string source = Path.Combine(_root, "plain.txt");
        File.WriteAllText(source, "hello\n", new UTF8Encoding(false));

        string planPath = Path.Combine(_root, $"plan-{field}.json");

        Assert.Equal(0, Run(
            "-BasePath", _root,
            "-Include", "plain.txt",
            "-Target", "utf-16-bom",
            "-Plan", planPath,
            "-Quiet"));

        JsonNode plan = JsonNode.Parse(File.ReadAllText(planPath))!;
        plan["Files"]![0]![field] = JsonValue.Create(value);

        File.WriteAllText(planPath, plan.ToJsonString(), new UTF8Encoding(false));

        return planPath;
    }

    [Theory]
    [InlineData("Action")]
    [InlineData("SourceInterpretation")]
    public void AnUndefinedEnumValueIsRefusedBeforeAnyFileIsTouched(string field)
    {
        string planPath = PlanWithField(field, 99);
        string source = Path.Combine(_root, "plain.txt");
        byte[] before = File.ReadAllBytes(source);

        string journal = Path.Combine(_root, $"journal-{field}.json");

        int exitCode = Run("-Apply", planPath, "-Journal", journal);

        Assert.NotEqual(0, exitCode);
        Assert.Equal(before, File.ReadAllBytes(source));

        // No journal may claim the rejected action ran.
        Assert.False(File.Exists(journal));
    }

    /// <summary>The result mapping must reject every undefined planned action.</summary>
    [Fact]
    public void AnUndefinedActionHasNoReportResult()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ConversionPolicy.ToRowResult((PlannedAction)99));
    }

    /// <summary>Every defined action still maps, or the throw would be a regression.</summary>
    [Fact]
    public void EveryDefinedActionStillMaps()
    {
        foreach (PlannedAction action in Enum.GetValues<PlannedAction>())
            _ = ConversionPolicy.ToRowResult(action);
    }
}
