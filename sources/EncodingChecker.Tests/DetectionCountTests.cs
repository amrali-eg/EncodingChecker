using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// A conversion plan must identify each selected file once, then carry out that approved
/// interpretation without detecting the file again.
///
/// View is informational. Planning deliberately takes one fresh, hash-bound snapshot of
/// each selected automatic-source file because detection is a heuristic and the file may
/// have changed since View. Once the user approves that plan, another detection pass could
/// produce a different answer from the one the user reviewed.
///
/// These tests count detection and policy classification separately. Counting makes the
/// no-second-pass rule an observable contract instead of an assumption about the design.
/// </summary>
public sealed class DetectionCountTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_counts_").FullName;

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

    private void Write(string name, string text, string charset) =>
        File.WriteAllBytes(
            Path.Combine(_root, name), Encoding.GetEncoding(charset).GetBytes(text));

    private void WriteThreeFiles()
    {
        Write("jp.txt", "こんにちは世界。日本語のテキストです。", "shift_jis");
        Write("zh.txt", "这是一段简体中文文本内容", "gb18030");
        Write("plain.txt", "plain ascii, no high bytes at all", "ascii");
    }

    /// <summary>Counts what one operation costs, from zero.</summary>
    private static (long Detections, long Classifications) Measure(Action operation)
    {
        DetectionCounters.Reset();
        operation();

        return (DetectionCounters.Detections, DetectionCounters.Classifications);
    }

    private List<ConversionReportEntry> View()
    {
        var scanned = new EntrySink();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                IncludeSubdirectories = true,
                IncludePatterns = ["*"],
                Action = ScanAction.Detect,
            },
            scanned.Add,
            CancellationToken.None);

        return [.. scanned];
    }

    [Fact]
    public void TheGuiSequenceWorksOutEachEncodingExactlyOnce()
    {
        WriteThreeFiles();

        List<ConversionReportEntry> entries = [];

        // Scenario: View identifies three files but makes no conversion decision.
        // A decision belongs to planning because it depends on the requested target.
        var view = Measure(() => entries = View());

        Assert.Equal(3, view.Detections);
        Assert.Equal(0, view.Classifications);

        long confirmations = 0;
        (long Detections, long Classifications) atConfirmation = (-1, -1);

        var convert = Measure(() =>
            new ConversionOrchestrator(plan =>
                {
                    confirmations++;

                    // Risk: a confirmation that re-derives data can disagree with the
                    // plan it presents. Reading this plan must not inspect any file again.
                    atConfirmation = (
                        DetectionCounters.Detections,
                        DetectionCounters.Classifications);

                    return ConfirmationResponse.Proceed;
                })
                .Run(
                    entries, _root, "utf-8", targetWriteBom: false,
                    backup: false, preview: false,
                    ScanEngine.DefaultMaxParallelism,
                    _ => { },
                    CancellationToken.None));

        Assert.Equal(1, confirmations);

        // Protection: planning refreshes each selected file once, then classifies that
        // exact snapshot.
        Assert.Equal(3, atConfirmation.Classifications);
        Assert.Equal(3, atConfirmation.Detections);

        // Carrying out the approved plan must add neither detection nor classification.
        Assert.Equal(3, convert.Classifications);
        Assert.Equal(3, convert.Detections);
    }

    [Fact]
    public void AnsweringARefusalClassifiesOnlyTheFilesItWasAskedAbout()
    {
        // An explicit source choice changes only the files the user selected; it must not
        // re-examine the rest of the batch.
        Write("jp.txt", "こんにちは世界。日本語のテキストです。", "shift_jis");
        Write("ambiguous.txt", "Le café était déjà prêt", "windows-1252");

        List<ConversionReportEntry> entries = View();
        var answered = false;

        var convert = Measure(() =>
            new ConversionOrchestrator(_ =>
                {
                    if (answered)
                        return ConfirmationResponse.Proceed;

                    answered = true;
                    return new ConfirmationResponse(
                        ConfirmationChoice.ChooseSourceEncoding,
                        "windows-1252",
                        [Path.Combine(_root, "ambiguous.txt")]);
                })
                .Run(
                    entries, _root, "utf-8", targetWriteBom: false,
                    backup: false, preview: false,
                    ScanEngine.DefaultMaxParallelism,
                    _ => { },
                    CancellationToken.None));

        // First pass: two detections. Re-plan: one classification for the file whose
        // source was chosen. The other entry retains its existing decision.
        Assert.Equal(3, convert.Classifications);
        Assert.Equal(2, convert.Detections);
    }

    [Fact]
    public void WritingAPlanDetectsOncePerFileAndApplyingItDetectsNothing()
    {
        WriteThreeFiles();

        string planPath = Path.Combine(_root, "plan.json");

        var plan = Measure(() => Assert.Equal(0, Cli(
            "-BasePath", _root, "-Target", "utf-8",
            "-Plan", planPath, "-Quiet")));

        Assert.Equal(3, plan.Detections);
        Assert.Equal(3, plan.Classifications);

        // Applying a plan checks that the recorded source is still present; it does not
        // return to the bytes to derive a new encoding decision.
        var apply = Measure(() => Assert.Equal(5, Cli("-Apply", planPath)));

        Assert.Equal(0, apply.Detections);
        Assert.Equal(0, apply.Classifications);
    }

    [Fact]
    public void AnOrdinaryCommandLineConversionAlsoWorksOutEachEncodingOnce()
    {
        WriteThreeFiles();

        var run = Measure(() => Assert.Equal(5, Cli(
            "-BasePath", _root, "-Target", "utf-8", "-Quiet")));

        Assert.Equal(3, run.Detections);
        Assert.Equal(3, run.Classifications);
    }

    [Fact]
    public void ReadingAPlanCostsNothing()
    {
        // Constructing and inspecting a plan must not reach the source bytes. The
        // confirmation dialog relies on this to show the already-approved decision.
        WriteThreeFiles();

        string planPath = Path.Combine(_root, "plan.json");

        Assert.Equal(0, Cli(
            "-BasePath", _root, "-Target", "utf-8", "-Plan", planPath, "-Quiet"));

        var read = Measure(() =>
        {
            ConversionPlan? loaded = ConversionPlan.Load(planPath, out _);

            Assert.NotNull(loaded);
            Assert.NotEmpty(loaded.Summarize());
            Assert.Empty(loaded.FindStaleFiles());
        });

        Assert.Equal(0, read.Detections);
        Assert.Equal(0, read.Classifications);
    }

    private static int Cli(params string[] args)
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
}
