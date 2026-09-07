using System.Text;
using System.Text.Json.Nodes;

namespace EncodingChecker.Tests;

/// <summary>
/// A plan carrying an action no build ever wrote must be refused, not reported as done.
/// </summary>
/// <remarks>
/// System.Text.Json accepts a number for any enum, so a hand-edited or damaged plan could
/// hold <c>"Action": 99</c> and load without complaint. It then reached
/// <c>ConversionPolicy.ToRowResult</c>, whose fallback arm was
/// <see cref="ConversionRowResult.Converted"/> - so the run exited 0, reported "1
/// converted", and wrote a journal asserting a conversion that never touched the file.
///
/// The journal is this product's audit trail. A journal that can claim work it did not do
/// is worse than one that is missing.
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

        // The point of the fix: nothing may record this as a conversion.
        Assert.False(File.Exists(journal));
    }

    /// <summary>
    /// The mapping names every action. Its fallback used to be Converted, which is how an
    /// undefined value became a reported success.
    /// </summary>
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
