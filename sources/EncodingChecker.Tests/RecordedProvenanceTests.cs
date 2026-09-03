using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// What the plan, the summary, and the sidecar say about a run must be true of it.
/// </summary>
public sealed class RecordedProvenanceTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_provenance_").FullName;

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

    private string Write(string name, string text, string charset)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, Encoding.GetEncoding(charset).GetBytes(text));

        return path;
    }

    private List<ConversionReportEntry> Scan()
    {
        var sink = new EntrySink();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions { BaseDirectory = _root, Action = ScanAction.Detect },
            sink.Add,
            CancellationToken.None);

        return sink.ToList();
    }

    /// <summary>
    /// Runs a preview, answering the review by choosing <paramref name="choices"/> in
    /// turn - one encoding per round, each scoped to the files still refused.
    /// </summary>
    private ConversionPlan? PlanAfterChoosing(
        List<ConversionReportEntry> entries, params string[] choices)
    {
        int round = 0;
        ConversionPlan? last = null;

        var orchestrator = new ConversionOrchestrator(plan =>
        {
            last = plan;

            if (round >= choices.Length)
                return ConfirmationResponse.Cancel;

            string charset = choices[round++];

            // Scope each answer to one refused file, which is what the dialog does and
            // what makes a mixed-source batch reachable at all.
            string? target = plan.Files
                .Where(f => f.NeedsSourceChoice)
                .Select(f => plan.ResolvePath(f))
                .FirstOrDefault(p => p is not null);

            return target is null
                ? ConfirmationResponse.Cancel
                : new ConfirmationResponse(
                    ConfirmationChoice.ChooseSourceEncoding, charset, [target]);
        });

        orchestrator.Run(
            entries, _root, "utf-8", targetWriteBom: false, backup: false,
            preview: false, maxParallelism: 1, onEntry: _ => { },
            CancellationToken.None);

        return last;
    }

    [Fact]
    public void ABatchResolvedWithSeveralEncodingsNamesNoneOfThemAsTheRunsSource()
    {
        // The GUI scopes each choice to the files it was ticked for, so a batch can end
        // with every file explicit and no two agreeing. Recording entries[0]'s label
        // stated one encoding for a run that used another.
        Write("a.txt", "café résumé façade", "windows-1252");
        Write("b.txt", "привет мир текст", "koi8-r");

        List<ConversionReportEntry> entries = Scan();

        Assert.Equal(2, entries.Count);

        ConversionPlan? plan = PlanAfterChoosing(entries, "windows-1252", "koi8-r");

        Assert.NotNull(plan);

        string[] chosen =
        [
            .. plan!.Files.Where(f => f.SourceWasSpecified)
                          .Select(f => f.SourceEncoding)
                          .Distinct(StringComparer.OrdinalIgnoreCase)
        ];

        Assert.Equal(2, chosen.Length);

        // No single answer, so the run-level field claims none. The per-file source
        // still carries the truth, and only it can describe a mixed run.
        Assert.Null(plan.ExplicitSourceEncoding);

        string summary = plan.Summarize();

        Assert.Contains("chosen per file", summary);
        Assert.Contains("windows-1252", summary);
        Assert.Contains("koi8-r", summary);
    }

    [Fact]
    public void OneEncodingForTheWholeBatchIsStillNamed()
    {
        // The control. Reporting null whenever anything was chosen would satisfy the
        // test above and throw away the case where a single answer is correct.
        Write("a.txt", "café résumé façade", "windows-1252");
        Write("b.txt", "naïve déjà vu", "windows-1252");

        ConversionPlan? plan =
            PlanAfterChoosing(Scan(), "windows-1252", "windows-1252");

        Assert.NotNull(plan);
        Assert.Equal("windows-1252", plan!.ExplicitSourceEncoding);
        Assert.Contains("windows-1252 (chosen by you", plan.Summarize());
    }

    [Fact]
    public void TheSummaryDoesNotClaimDetectionWasBypassed()
    {
        // Detection is replaced as the codec used, not skipped: it still runs and its
        // result is recorded, and it is what the conflicting-source refusal and the
        // BOM-less advisories are decided against. EC-01 was that check failing to
        // reach an applied plan, so a summary announcing its absence is exactly wrong.
        Write("a.txt", "café résumé façade", "windows-1252");

        ConversionPlan? plan = PlanAfterChoosing(Scan(), "windows-1252");

        Assert.NotNull(plan);

        string summary = plan!.Summarize();

        Assert.DoesNotContain("bypassed", summary);
        Assert.Contains("detection still ran", summary);

        // And it did run: the plan carries what it concluded.
        Assert.NotNull(Assert.Single(plan.Files).DetectedEncoding);
    }

    [Fact]
    public void NothingChosenIsStillDescribedAsDetected()
    {
        Write("a.txt", "plain ascii text", "ascii");

        ConversionPlan plan = ConversionPlan.FromEntries(
            RunPolicyOver(Scan()), _root, "utf-8", targetHasBom: false,
            backupEnabled: false, explicitSource: null);

        Assert.Contains("detected per file", plan.Summarize());
    }

    [Fact]
    public void TheSidecarsOutputHashMatchesTheBytesThatWereInstalled()
    {
        // EC-14. The record carried the source digest in both fields, so its two hashes
        // were one measurement written twice and a reader could not tell that anything
        // had been compared. The value is the same either way - verification has already
        // proven the texts equal - so the assertion has to come from outside: decode the
        // installed file and hash it the way the converter does.
        string path = Write("legacy.txt", "café résumé façade", "windows-1252");

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            SourceCharset = "windows-1252",
            TargetCharset = "utf-8",
            Backup = true,
        };

        var sink = new EntrySink();
        ScanEngine.ScanDirectory(options, sink.Add, CancellationToken.None);

        Assert.Equal(ConversionRowResult.Converted, Assert.Single(sink).Result);

        ConversionMetadata sidecar = System.Text.Json.JsonSerializer
            .Deserialize<ConversionMetadata>(
                File.ReadAllText(ConversionMetadataStore.MetadataPathFor(path)))!;

        string installed = DecodedTextSha256(path, new UTF8Encoding(false));

        Assert.Equal(installed, sidecar.OutputTextSha256);
        Assert.Equal(installed, sidecar.SourceTextSha256);
    }

    /// <summary>
    /// SHA-256 over the decoded text, matching how the converter digests content.
    /// </summary>
    private static string DecodedTextSha256(string path, Encoding encoding)
    {
        char[] chars = encoding.GetChars(File.ReadAllBytes(path));

        return Convert.ToHexStringLower(
            SHA256.HashData(MemoryMarshal.AsBytes<char>(chars)));
    }

    /// <summary>Decides the entries without writing, so they can be planned.</summary>
    private List<ConversionReportEntry> RunPolicyOver(List<ConversionReportEntry> entries)
    {
        ScanEngine.ConvertFiles(
            entries, "utf-8", targetWriteBom: false, maxParallelism: 1,
            whatIf: true, backup: false, _ => { }, CancellationToken.None);

        return entries;
    }
}
