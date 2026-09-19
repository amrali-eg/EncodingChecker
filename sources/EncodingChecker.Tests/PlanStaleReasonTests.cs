using System.Text;
using System.Text.Json.Nodes;

namespace EncodingChecker.Tests;

/// <summary>
/// A plan that no longer describes the files and codecs in front of it is refused, and each
/// reason it can go stale is reported as its own message.
/// </summary>
/// <remarks>
/// Applying a plan is the one place EC writes files it did not just look at, so the plan carries
/// what it was reviewed against - the target codec, each file's hash, the source codec and how
/// it was detected - and <see cref="ConversionPlan.FindStaleFiles"/> checks every one of them
/// again. Each case here edits one recorded fact in a plan that is otherwise valid and asserts
/// exactly one stale message, so a check that is dropped fails its own test, and a check that
/// fires for the wrong reason fails the control.
/// <para>
/// The tests load a plan EC really wrote, then change it with <c>with</c> expressions instead of
/// writing JSON, since the records are init-only.
/// </para>
/// </remarks>
public sealed class PlanStaleReasonTests : IDisposable
{
    private const string NoSuchCharset = "not-a-real-charset";

    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_stale_").FullName;

    private string PlanPath => Path.Combine(_root, "plan.json");

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

    // A UTF-8 file with a BOM, so converting to UTF-8 rewrites it and the plan schedules a Convert.
    private string WriteSource(string name = "a.txt")
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, "hello world", new UTF8Encoding(true));

        return path;
    }

    private ConversionPlan MakePlan()
    {
        WriteSource();

        Assert.Equal(
            0,
            CliRunner.Run("-BasePath", _root, "-Target", "utf-8", "-Plan", PlanPath, "-Quiet"));

        ConversionPlan? plan = ConversionPlan.Load(PlanPath, out string? error);
        Assert.Null(error);
        Assert.NotNull(plan);

        PlannedFile file = Assert.Single(plan.Files);
        Assert.Equal(PlannedAction.Convert, file.Action);

        return plan;
    }

    // Pins the recorded detection to a known state, so each case below changes one fact from a
    // baseline that is itself valid.
    private static ConversionPlan WithDetectedUtf8(ConversionPlan plan) =>
        Replace(plan, plan.Files[0] with
        {
            DetectedEncoding = "utf-8",
            DetectedCodePage = 65001,
            DetectedHasBom = true,
        });

    private static ConversionPlan Replace(ConversionPlan plan, params PlannedFile[] files) =>
        plan with { Files = files };

    private static string OnlyMessage(ConversionPlan plan) =>
        Assert.Single(plan.FindStaleFiles());

    [Fact]
    public void ThePlanUsedAsABaselineIsNotStale()
    {
        // The control: every test below changes one thing in this plan and expects one message.
        ConversionPlan plan = WithDetectedUtf8(MakePlan());

        Assert.Empty(plan.FindStaleFiles());
    }

    [Fact]
    public void ATargetEncodingThatIsNoLongerAvailable_IsStale()
    {
        ConversionPlan plan = WithDetectedUtf8(MakePlan()) with { TargetEncoding = NoSuchCharset };

        Assert.Equal(
            $"Target encoding '{NoSuchCharset}' is not available.",
            OnlyMessage(plan));
    }

    [Fact]
    public void AFileListedTwice_IsStaleTheSecondTime()
    {
        ConversionPlan plan = WithDetectedUtf8(MakePlan());
        plan = Replace(plan, plan.Files[0], plan.Files[0]);

        Assert.EndsWith("(appears more than once in the plan)", OnlyMessage(plan));
    }

    [Fact]
    public void ASourceEncodingThatIsNoLongerAvailable_IsStale()
    {
        ConversionPlan plan = WithDetectedUtf8(MakePlan());
        plan = Replace(plan, plan.Files[0] with { SourceEncoding = NoSuchCharset });

        Assert.EndsWith(
            $"(source encoding '{NoSuchCharset}' is not available)",
            OnlyMessage(plan));
    }

    [Fact]
    public void ASourceCodecWhoseIdentityChanged_IsStale()
    {
        // The name still resolves, but to a different code page than the one the plan recorded.
        ConversionPlan plan = WithDetectedUtf8(MakePlan());
        plan = Replace(plan, plan.Files[0] with { SourceCodePage = 1252 });

        Assert.EndsWith(
            "(source codec identity does not match the plan)",
            OnlyMessage(plan));
    }

    [Theory]
    [InlineData("otherCodePage")]
    [InlineData("noCodePage")]
    [InlineData("unknownName")]
    public void ADetectedCodecWhoseIdentityChanged_IsStale(string change)
    {
        ConversionPlan plan = WithDetectedUtf8(MakePlan());

        PlannedFile changed = change switch
        {
            "otherCodePage" => plan.Files[0] with { DetectedCodePage = 1252 },
            "noCodePage" => plan.Files[0] with { DetectedCodePage = null },
            _ => plan.Files[0] with { DetectedEncoding = NoSuchCharset },
        };

        Assert.EndsWith(
            "(detected codec identity does not match the plan)",
            OnlyMessage(Replace(plan, changed)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DetectionDetailsWithoutADetectedCodec_AreStale(bool keepCodePage, bool keepBom)
    {
        // A recorded code page or BOM with no detected encoding to attach it to.
        ConversionPlan plan = WithDetectedUtf8(MakePlan());
        plan = Replace(
            plan,
            plan.Files[0] with
            {
                DetectedEncoding = null,
                DetectedCodePage = keepCodePage ? 65001 : null,
                DetectedHasBom = keepBom,
            });

        Assert.EndsWith(
            "(detected codec provenance is incomplete)",
            OnlyMessage(plan));
    }

    [Fact]
    public void AFileThatCannotBeReadForItsHash_IsStaleWithTheReasonTheSystemGave()
    {
        ConversionPlan plan = WithDetectedUtf8(MakePlan());
        string path = Path.Combine(_root, "a.txt");

        // A second open is refused exactly as it would be while another process holds the file.
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        string message = OnlyMessage(plan);

        Assert.StartsWith(path, message);
        Assert.DoesNotContain("contents changed", message);
        Assert.DoesNotContain("no longer exists", message);
    }

    [Fact]
    public void ARelativePathTheSystemCannotResolve_IsReportedAsLeavingThePlansDirectory()
    {
        // An embedded NUL is rejected by path resolution itself, before any containment check.
        ConversionPlan plan = WithDetectedUtf8(MakePlan());
        PlannedFile file = plan.Files[0] with { RelativePath = "bad\0name.txt" };
        plan = Replace(plan, file);

        Assert.Null(plan.ResolvePath(file));
        Assert.EndsWith("(resolves outside the plan's directory)", OnlyMessage(plan));
    }

    [Fact]
    public void AnEmptyPlanFile_IsRefusedAsEmpty()
    {
        File.WriteAllText(PlanPath, "null");

        ConversionPlan? plan = ConversionPlan.Load(PlanPath, out string? error);

        Assert.Null(plan);
        Assert.Equal($"'{PlanPath}' is empty.", error);
    }

    [Theory]
    [InlineData("BaseDirectory", "")]
    [InlineData("TargetEncoding", "  ")]
    [InlineData("Files", null)]
    public void APlanMissingRequiredConversionInformation_IsRefused(string field, string? value)
    {
        MakePlan();

        JsonObject document = JsonNode.Parse(File.ReadAllText(PlanPath))!.AsObject();
        document[field] = value;
        File.WriteAllText(PlanPath, document.ToJsonString());

        ConversionPlan? plan = ConversionPlan.Load(PlanPath, out string? error);

        Assert.Null(plan);
        Assert.Equal("The plan is missing required conversion information.", error);
    }
}
