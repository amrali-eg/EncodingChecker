using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// EC works out a file's encoding exactly once, and never again between the moment a
/// decision is approved and the moment it is carried out.
///
/// That property is what makes a preview a promise rather than a demonstration. A second
/// pass over the same bytes can answer differently — detection is a heuristic — and it
/// was the first answer the user agreed to. Every surface is built not to do it. But
/// "built not to" is an architectural claim, and this project has already been caught by
/// one of those: the GUI was built to apply the ambiguity refusal too, and did not.
///
/// So it is counted. Two counts, because they are separate questions that fail the same
/// way: <em>which encoding is this</em>, and <em>do the bytes settle it</em>.
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
        var scanned = new List<ConversionReportEntry>();

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

        return scanned;
    }

    [Fact]
    public void TheGuiSequenceWorksOutEachEncodingExactlyOnce()
    {
        WriteThreeFiles();

        List<ConversionReportEntry> entries = [];

        // View: three files, three detections, and nothing classified yet - which is
        // precisely why the classification cannot live in the scan.
        var view = Measure(() => entries = View());

        Assert.Equal(3, view.Detections);
        Assert.Equal(0, view.Classifications);

        long confirmations = 0;
        (long Detections, long Classifications) atConfirmation = (-1, -1);

        var convert = Measure(() =>
            new ConversionOrchestrator(plan =>
                {
                    confirmations++;

                    // Building and reading the plan must cost nothing. A confirmation
                    // that re-derives anything is a confirmation that can disagree with
                    // what it is confirming.
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

        // Classified once each by the deciding pass; detected not at all, because View
        // already answered that and the entries carry the answer.
        Assert.Equal(3, atConfirmation.Classifications);
        Assert.Equal(0, atConfirmation.Detections);

        // And the pass that actually writes adds nothing to either count.
        Assert.Equal(3, convert.Classifications);
        Assert.Equal(0, convert.Detections);
    }

    [Fact]
    public void AnsweringARefusalClassifiesOnlyTheFilesItWasAskedAbout()
    {
        // Re-planning after an explicit choice must not re-examine the whole batch.
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
                        ConfirmationChoice.ChooseSourceEncoding, "windows-1252");
                })
                .Run(
                    entries, _root, "utf-8", targetWriteBom: false,
                    backup: false, preview: false,
                    ScanEngine.DefaultMaxParallelism,
                    _ => { },
                    CancellationToken.None));

        // Two on the first pass. The re-plan classifies nothing at all: the file the user
        // named needs no examining, and the other one keeps the decision it already had.
        Assert.Equal(2, convert.Classifications);
        Assert.Equal(0, convert.Detections);
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

        // The whole point of a plan. Applying it re-asserts what was recorded; it does
        // not go back to the bytes to ask again.
        var apply = Measure(() => Assert.Equal(0, Cli("-Apply", planPath)));

        Assert.Equal(0, apply.Detections);
        Assert.Equal(0, apply.Classifications);
    }

    [Fact]
    public void AnOrdinaryCommandLineConversionAlsoWorksOutEachEncodingOnce()
    {
        WriteThreeFiles();

        var run = Measure(() => Assert.Equal(0, Cli(
            "-BasePath", _root, "-Target", "utf-8", "-Quiet")));

        Assert.Equal(3, run.Detections);
        Assert.Equal(3, run.Classifications);
    }

    [Fact]
    public void ReadingAPlanCostsNothing()
    {
        // Isolated from the surrounding sequence: constructing and inspecting a plan must
        // never reach the bytes. This is the property the confirmation dialog rests on.
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
