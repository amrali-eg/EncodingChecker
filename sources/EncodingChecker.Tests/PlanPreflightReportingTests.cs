using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// A plan run must report files it could not read, and a plan holding one must remain
/// usable for the files it could.
/// </summary>
/// <remarks>
/// Two defects met here. The plan branch returned before the <c>Error</c> check, so a
/// run that never opened some of its files exited 0 - the same scan without
/// <c>-Plan</c> exited 3. And planning deliberately records an unreadable file as a
/// refusal with an empty hash, "rather than silently dropping" it, while loading
/// rejected any entry without one. A single transiently locked file therefore produced
/// a plan that reported success and could never be applied, taking every readable file
/// with it.
/// <para>
/// The fixture holds a real exclusive handle rather than simulating the failure, so
/// what is exercised is the same <see cref="IOException"/> path a locked file produces
/// in the field. A second open in the same process is blocked by
/// <see cref="FileShare.None"/> exactly as it would be from another one.
/// </para>
/// </remarks>
public sealed class PlanPreflightReportingTests : IDisposable
{
    private const int ExpectedClean = 0;
    private const int ExpectedUsageError = 1;
    private const int ExpectedChangesNeeded = 2;
    private const int ExpectedProcessingErrors = 3;
    private const int ExpectedSafeRefusal = 5;

    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_planpreflight_").FullName;

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

    private static int Run(out string stderr, params string[] args)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;

        try
        {
            using var errors = new StringWriter();
            Console.SetOut(new StringWriter());
            Console.SetError(errors);

            int exitCode = Program.RunConsoleMode(args);
            stderr = errors.ToString();

            return exitCode;
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static int Run(params string[] args) => Run(out _, args);

    /// <summary>A UTF-8 file with a BOM, so converting to utf-8 rewrites it.</summary>
    private string Write(string name)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, "hello world", new UTF8Encoding(true));

        return path;
    }

    /// <summary>Denies every other handle, producing the same failure as a locked file.</summary>
    private static FileStream HoldExclusively(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.None);

    private static bool StillHasBom(string path) =>
        File.ReadAllBytes(path).Take(3).SequenceEqual(Encoding.UTF8.GetPreamble());

    [Fact]
    public void PlanOverAnUnreadableFile_ExitsThreeLikeTheSameScanWithoutPlan()
    {
        Write("good.txt");
        string locked = Write("locked.txt");
        string planPath = Path.Combine(_root, "plan.json");

        using (HoldExclusively(locked))
        {
            // The control is the same scan without -Plan: whatever it reports, the plan
            // run must not report better.
            Assert.Equal(
                ExpectedProcessingErrors,
                Run("-BasePath", _root, "-Target", "utf-8", "-WhatIf", "-Quiet"));

            Assert.Equal(
                ExpectedProcessingErrors,
                Run("-BasePath", _root, "-Target", "utf-8", "-Plan", planPath, "-Quiet"));
        }

        // The plan is still written and still names the file, so exit 3 is diagnosable.
        Assert.True(File.Exists(planPath));
    }

    [Fact]
    public void TheUnreadableFileIsNamedOnStderrEvenWhenQuiet()
    {
        // -Quiet suppresses the CSV, which was the only place the reason appeared. An
        // exit code with nothing to act on is not a report.
        string locked = Write("locked.txt");

        using (HoldExclusively(locked))
        {
            Run(out string stderr, "-BasePath", _root, "-Target", "utf-8", "-Quiet");

            Assert.Contains("locked.txt", stderr);
        }
    }

    [Fact]
    public void ErrorsOutrankFailOnChanges()
    {
        // Published precedence is 3 before 2. A run that both needs changes and failed
        // to read something must report the failure.
        Write("good.txt");
        string locked = Write("locked.txt");
        string planPath = Path.Combine(_root, "plan.json");

        using (HoldExclusively(locked))
        {
            Assert.Equal(
                ExpectedProcessingErrors,
                Run("-BasePath", _root, "-Target", "utf-8", "-FailOnChanges",
                    "-Plan", planPath, "-Quiet"));
        }
    }

    [Fact]
    public void ACleanPlanStillExitsZeroAndFailOnChangesStillReturnsTwo()
    {
        // The control. Returning 3 unconditionally would satisfy every test above.
        Write("good.txt");
        string planPath = Path.Combine(_root, "plan.json");

        Assert.Equal(
            ExpectedClean,
            Run("-BasePath", _root, "-Target", "utf-8", "-Plan", planPath, "-Quiet"));

        Assert.Equal(
            ExpectedChangesNeeded,
            Run("-BasePath", _root, "-Target", "utf-8", "-FailOnChanges",
                "-Plan", planPath, "-Quiet"));
    }

    [Fact]
    public void APlanHoldingAnUnreadableFileStillAppliesTheReadableOnes()
    {
        string good = Write("good.txt");
        string locked = Write("locked.txt");
        string planPath = Path.Combine(_root, "plan.json");

        using (HoldExclusively(locked))
        {
            Run("-BasePath", _root, "-Target", "utf-8", "-Plan", planPath, "-Quiet");
        }

        ConversionPlan? plan = ConversionPlan.Load(planPath, out string? error);

        Assert.Null(error);
        Assert.NotNull(plan);

        PlannedFile unreadable =
            Assert.Single(plan!.Files, f => f.RelativePath == "locked.txt");

        // Recorded, visible, and making no claim about bytes it could not read.
        Assert.Equal(PlannedAction.Refuse, unreadable.Action);
        Assert.Equal(string.Empty, unreadable.Sha256);

        // The hashless refusal must not be reported as changed content.
        Assert.Empty(plan.FindStaleFiles());

        Assert.Equal(ExpectedSafeRefusal, Run("-Apply", planPath));

        Assert.False(StillHasBom(good));
        Assert.True(StillHasBom(locked));
    }

    [Fact]
    public void APlanSchedulingAConversionWithoutAHashIsStillRefused()
    {
        // The safety property must not be weakened by the fix. A hash is what pins a
        // conversion to reviewed content, so an entry that will be written still needs
        // one; only entries that will not be written are exempt.
        Write("good.txt");
        string planPath = Path.Combine(_root, "plan.json");

        Run("-BasePath", _root, "-Target", "utf-8", "-Plan", planPath, "-Quiet");

        string json = File.ReadAllText(planPath);
        string hash = ConversionMetadataStore.ComputeSha256(
            Path.Combine(_root, "good.txt"));

        Assert.Contains(hash, json, StringComparison.OrdinalIgnoreCase);

        string stripped = json.Replace(hash, string.Empty, StringComparison.OrdinalIgnoreCase);

        Assert.NotEqual(json, stripped);
        File.WriteAllText(planPath, stripped);

        Assert.Null(ConversionPlan.Load(planPath, out string? error));
        Assert.Contains("good.txt", error);
        Assert.Equal(ExpectedUsageError, Run("-Apply", planPath));
    }

    [Fact]
    public void ARefusalThatDidRecordAHashStillGoesStaleWhenItChanges()
    {
        // Exempting hashless entries must not exempt entries that have one. A plan is
        // reviewed as a whole, so a refused file whose bytes moved still invalidates it.
        //
        // BOM-less UTF-16 whose byte order cannot be proven is the dependable refusal
        // fixture. Short legacy text is not: sample-based detection accepts a truncated
        // trailing sequence, so "café" in windows-1252 reads as UTF-8 and plans as
        // Unchanged rather than as a refusal.
        string path = Path.Combine(_root, "ambiguous.txt");
        File.WriteAllBytes(
            path,
            new UnicodeEncoding(bigEndian: false, byteOrderMark: false)
                .GetBytes("Hello World, this is plain text."));
        string planPath = Path.Combine(_root, "plan.json");

        Run("-BasePath", _root, "-Target", "utf-8", "-Plan", planPath, "-Quiet");

        ConversionPlan plan = ConversionPlan.Load(planPath, out _)!;
        PlannedFile refused = Assert.Single(plan.Files);

        Assert.Equal(PlannedAction.Refuse, refused.Action);
        Assert.NotEqual(string.Empty, refused.Sha256);
        Assert.Empty(plan.FindStaleFiles());

        File.WriteAllBytes(
            path,
            new UnicodeEncoding(bigEndian: false, byteOrderMark: false)
                .GetBytes("Hello World, this is different text now."));

        Assert.Contains(
            "contents changed",
            Assert.Single(ConversionPlan.Load(planPath, out _)!.FindStaleFiles()));
    }
}
