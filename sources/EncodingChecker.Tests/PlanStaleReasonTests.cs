using System.Text;
using System.Text.Json.Nodes;

namespace EncodingChecker.Tests;

/// <summary>
/// A plan that no longer describes the target codec, the files or their codecs is refused, and
/// each reason it can go stale is reported as its own message.
/// </summary>
/// <remarks>
/// A plan is applied later than it was reviewed, so it records what it was reviewed against.
/// <see cref="ConversionPlan.FindStaleFiles"/> checks that the target codec is still available
/// and, for each file, that its path stays inside the plan's directory and is listed once, that
/// the file still exists without a link in its path and still has the recorded hash, that the
/// source codec (for a file scheduled for conversion) still resolves to the recorded code page,
/// and that the recorded detected codec still resolves to the recorded code page and is
/// recorded completely. It does not consult the recorded BOM flags or whether a source was
/// chosen.
/// <para>
/// Each stale-reason case loads a plan EC really wrote, changes one recorded fact with a
/// <c>with</c> expression (the records are init-only), and asserts the one message that fact
/// should produce, in full. The untouched plan is checked to be clean first, so a message can
/// only come from the change. The two-field detection theory, the held file and the load
/// errors change a condition instead of a single fact.
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
            // A leftover temp directory must not fail the test run.
        }
    }

    // The path a stale message names for a file in the plan's directory.
    private string PathOf(string name) => Path.GetFullPath(Path.Combine(_root, name));

    // A UTF-8 file with a BOM, so converting to UTF-8 rewrites it and the plan schedules a Convert.
    private void WriteSource(string name)
    {
        File.WriteAllText(Path.Combine(_root, name), "hello world", new UTF8Encoding(true));
    }

    private ConversionPlan MakePlan(params string[] names)
    {
        foreach (string name in names.Length == 0 ? new[] { "a.txt" } : names)
        {
            WriteSource(name);
        }

        Assert.Equal(
            0,
            CliRunner.Run("-BasePath", _root, "-Target", "utf-8", "-Plan", PlanPath, "-Quiet"));

        ConversionPlan? plan = ConversionPlan.Load(PlanPath, out string? error);
        Assert.Null(error);
        Assert.NotNull(plan);

        // The facts the cases below rely on are checked here, so a change that stopped EC
        // recording them fails loudly instead of leaving every case testing nothing.
        Assert.NotEmpty(plan.Files);

        foreach (PlannedFile file in plan.Files)
        {
            Assert.Equal(PlannedAction.Convert, file.Action);
            Assert.Equal("utf-8", file.SourceEncoding);
            Assert.Equal(65001, file.SourceCodePage);
            Assert.Equal("utf-8", file.DetectedEncoding);
            Assert.Equal(65001, file.DetectedCodePage);
            Assert.True(file.DetectedHasBom);
        }

        Assert.Empty(plan.FindStaleFiles());

        return plan;
    }

    private static ConversionPlan Replace(ConversionPlan plan, params PlannedFile[] files) =>
        plan with { Files = files };

    private static string OnlyMessage(ConversionPlan plan) =>
        Assert.Single(plan.FindStaleFiles());

    [Fact]
    public void ATargetEncodingThatIsNoLongerAvailable_IsStale()
    {
        ConversionPlan plan = MakePlan() with { TargetEncoding = NoSuchCharset };

        Assert.Equal(
            $"Target encoding '{NoSuchCharset}' is not available.",
            OnlyMessage(plan));
    }

    [Fact]
    public void AFileListedTwice_IsStaleTheSecondTime()
    {
        ConversionPlan plan = MakePlan();
        plan = Replace(plan, plan.Files[0], plan.Files[0]);

        Assert.Equal(
            $"{PathOf("a.txt")} (appears more than once in the plan)",
            OnlyMessage(plan));
    }

    [Fact]
    public void AFileListedTwiceUnderDifferentCase_IsStaleTheSecondTime()
    {
        // Paths are compared without regard to case, as the file system does.
        ConversionPlan plan = MakePlan();
        plan = Replace(plan, plan.Files[0], plan.Files[0] with { RelativePath = "A.TXT" });

        Assert.Equal(
            $"{PathOf("A.TXT")} (appears more than once in the plan)",
            OnlyMessage(plan));
    }

    [Fact]
    public void ASourceEncodingThatIsNoLongerAvailable_IsStale()
    {
        ConversionPlan plan = MakePlan();
        plan = Replace(plan, plan.Files[0] with { SourceEncoding = NoSuchCharset });

        Assert.Equal(
            $"{PathOf("a.txt")} (source encoding '{NoSuchCharset}' is not available)",
            OnlyMessage(plan));
    }

    [Fact]
    public void ASourceCodecWhoseIdentityChanged_IsStale()
    {
        // The name still resolves, but to a different code page than the one the plan recorded.
        ConversionPlan plan = MakePlan();
        plan = Replace(plan, plan.Files[0] with { SourceCodePage = 1252 });

        Assert.Equal(
            $"{PathOf("a.txt")} (source codec identity does not match the plan)",
            OnlyMessage(plan));
    }

    [Theory]
    [InlineData("otherCodePage")]
    [InlineData("noCodePage")]
    [InlineData("unknownName")]
    public void ADetectedCodecWhoseIdentityChanged_IsStale(string change)
    {
        ConversionPlan plan = MakePlan();

        PlannedFile changed = change switch
        {
            "otherCodePage" => plan.Files[0] with { DetectedCodePage = 1252 },
            "noCodePage" => plan.Files[0] with { DetectedCodePage = null },
            _ => plan.Files[0] with { DetectedEncoding = NoSuchCharset },
        };

        Assert.Equal(
            $"{PathOf("a.txt")} (detected codec identity does not match the plan)",
            OnlyMessage(Replace(plan, changed)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DetectionDetailsWithoutADetectedCodec_AreStale(bool keepCodePage, bool keepBom)
    {
        // A recorded code page or BOM with no detected encoding to attach it to.
        ConversionPlan plan = MakePlan();
        plan = Replace(
            plan,
            plan.Files[0] with
            {
                DetectedEncoding = null,
                DetectedCodePage = keepCodePage ? 65001 : null,
                DetectedHasBom = keepBom,
            });

        Assert.Equal(
            $"{PathOf("a.txt")} (detected codec provenance is incomplete)",
            OnlyMessage(plan));
    }

    [Fact]
    public void AFileThatCannotBeReadForItsHash_IsStaleWithTheReasonTheSystemGave()
    {
        ConversionPlan plan = MakePlan();
        string path = PathOf("a.txt");

        // A second open is refused exactly as it would be while another process holds the file.
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        // The system's own reason, obtained the same way the plan check obtains it, so the
        // message is pinned in full and cannot be mistaken for the link or hash messages.
        string reason = Assert.Throws<IOException>(
            () => ConversionMetadataStore.ComputeSha256(path)).Message;

        Assert.Equal($"{path} ({reason})", OnlyMessage(plan));
    }

    [Fact]
    public void ARelativePathTheSystemCannotResolve_IsReportedAsLeavingThePlansDirectory()
    {
        // An embedded NUL is rejected by path resolution itself, before any containment check.
        ConversionPlan plan = MakePlan();
        PlannedFile file = plan.Files[0] with { RelativePath = "bad\0name.txt" };
        plan = Replace(plan, file);

        Assert.Null(plan.ResolvePath(file));
        Assert.Equal(
            "bad\0name.txt (resolves outside the plan's directory)",
            OnlyMessage(plan));
    }

    [Fact]
    public void EveryStaleFileIsReported_NotJustTheFirst()
    {
        // One bad target and three files, each stale for a different reason. Any of these
        // returning early would drop the messages after it.
        ConversionPlan plan = MakePlan("a.txt", "b.txt", "c.txt");

        File.Delete(PathOf("b.txt"));
        File.WriteAllText(PathOf("c.txt"), "changed since the plan", new UTF8Encoding(true));

        plan = plan with
        {
            TargetEncoding = NoSuchCharset,
            Files =
            [
                .. plan.Files.Select(f => f.RelativePath == "a.txt"
                    ? f with { SourceCodePage = 1252 }
                    : f),
            ],
        };

        string[] expected =
        [
            $"Target encoding '{NoSuchCharset}' is not available.",
            $"{PathOf("a.txt")} (source codec identity does not match the plan)",
            $"{PathOf("b.txt")} (no longer exists)",
            $"{PathOf("c.txt")} (contents changed since the plan was made)",
        ];

        Assert.Equal(expected, plan.FindStaleFiles());
    }

    [Fact]
    public void AnEntryThatIsNotScheduledForConversion_IsNotHeldToTheSourceCodecChecks()
    {
        // A refusal writes nothing, so its source codec is a note, not a promise. Its hash is
        // still checked when it has one, and a refusal recorded without one is left alone.
        ConversionPlan plan = MakePlan();
        PlannedFile refusal = plan.Files[0] with
        {
            Action = PlannedAction.Refuse,
            SourceEncoding = NoSuchCharset,
            SourceCodePage = 1,
        };

        Assert.Empty(Replace(plan, refusal).FindStaleFiles());
        Assert.Empty(Replace(plan, refusal with { Sha256 = "" }).FindStaleFiles());

        Assert.Equal(
            $"{PathOf("a.txt")} (contents changed since the plan was made)",
            OnlyMessage(Replace(plan, refusal with { Sha256 = new string('0', 64) })));
    }

    [Fact]
    public void AnEmptyFileList_IsNotStale_ButTheTargetIsStillChecked()
    {
        ConversionPlan plan = Replace(MakePlan());

        Assert.Empty(plan.FindStaleFiles());
        Assert.Equal(
            $"Target encoding '{NoSuchCharset}' is not available.",
            OnlyMessage(plan with { TargetEncoding = NoSuchCharset }));
    }

    [Fact]
    public void ANullPlanDocument_IsRefusedAsEmpty()
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
