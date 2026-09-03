using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// The combinations docs/CLI.md calls invalid must actually be rejected.
/// </summary>
/// <remarks>
/// The documentation stated a contract the validator only partly enforced. Five of the
/// eight options named for <c>-DetectOnly</c> were rejected; <c>-Target</c>,
/// <c>-WhatIf</c> and <c>-Backup</c> were accepted and silently ignored, as were
/// <c>-WhatIf</c> and <c>-Backup</c> under <c>-Validate</c>, and <c>-Quiet</c> with
/// <c>-Verbose</c>.
/// <para>
/// Nothing destructive happened, because the read-only modes write nothing whatever
/// they are handed. The cost is to automation: a script that meant to convert, and
/// passed <c>-DetectOnly</c> by mistake or left it in, got exit 0 and a clean report
/// and no indication that its <c>-Target</c> had been dropped.
/// </para>
/// </remarks>
public sealed class DocumentedOptionContractTests : IDisposable
{
    private const int ExpectedClean = 0;
    private const int ExpectedUsageError = 1;

    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_optioncontract_").FullName;

    public DocumentedOptionContractTests() =>
        File.WriteAllText(
            Path.Combine(_root, "a.txt"), "plain ascii", new UTF8Encoding(false));

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

    [Theory]
    [InlineData("-Validate", "us-ascii")]
    [InlineData("-Target", "utf-8")]
    [InlineData("-From", "windows-1252")]
    [InlineData("-WhatIf", null)]
    [InlineData("-Backup", null)]
    [InlineData("-Plan", "plan.json")]
    [InlineData("-Apply", "plan.json")]
    [InlineData("-Journal", "journal.json")]
    public void DetectOnlyRejectsEveryOptionTheDocumentationNames(string flag, string? value)
    {
        // The full list from docs/CLI.md, so the two cannot drift apart unnoticed.
        string[] args = value is null
            ? ["-BasePath", _root, "-DetectOnly", flag]
            : ["-BasePath", _root, "-DetectOnly", flag, Resolve(value)];

        Assert.Equal(ExpectedUsageError, Run(out string stderr, args));
        Assert.Contains(flag, stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// Turns a fixture value into a usable one, creating a real plan where the option
    /// needs one.
    /// </summary>
    /// <remarks>
    /// <c>-Apply</c> checks that the plan exists before it checks what it was combined
    /// with, so a placeholder path is rejected for the wrong reason and proves nothing
    /// about the conflict.
    /// </remarks>
    private string Resolve(string value)
    {
        if (value != "plan.json")
            return value;

        string planPath = Path.Combine(_root, "real-plan.json");

        if (!File.Exists(planPath))
        {
            Assert.Equal(
                ExpectedClean,
                Run("-BasePath", _root, "-Target", "utf-8", "-Plan", planPath, "-Quiet"));
        }

        return planPath;
    }

    [Theory]
    [InlineData("-Target", "utf-8")]
    [InlineData("-From", "windows-1252")]
    [InlineData("-WhatIf", null)]
    [InlineData("-Backup", null)]
    public void ValidateRejectsConversionOptions(string flag, string? value)
    {
        string[] args = value is null
            ? ["-BasePath", _root, "-Validate", "us-ascii", flag]
            : ["-BasePath", _root, "-Validate", "us-ascii", flag, value];

        Assert.Equal(ExpectedUsageError, Run(out string stderr, args));
        Assert.Contains(flag, stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void QuietAndVerboseCannotBeCombined()
    {
        Assert.Equal(
            ExpectedUsageError,
            Run(out string stderr, "-BasePath", _root, "-DetectOnly", "-Quiet", "-Verbose"));

        Assert.Contains("-Quiet", stderr, StringComparison.Ordinal);
        Assert.Contains("-Verbose", stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-DetectOnly")]
    [InlineData("-DetectOnly", "-Quiet")]
    [InlineData("-DetectOnly", "-Verbose")]
    [InlineData("-DetectOnly", "-FailOnChanges")]
    [InlineData("-DetectOnly", "-MaxParallelism", "2")]
    [InlineData("-Validate", "us-ascii")]
    [InlineData("-Validate", "us-ascii", "-FailOnChanges")]
    [InlineData("-Validate", "us-ascii", "-Quiet")]
    public void CombinationsTheDocumentationAllowsStillWork(params string[] rest)
    {
        // The control. Rejecting every pair would satisfy both theories above while
        // making the read-only modes unusable.
        Assert.Equal(ExpectedClean, Run(["-BasePath", _root, .. rest]));
    }

    [Fact]
    public void TheAmbiguousRefusalNamesBothByteOrdersAndNeitherIsEcsOwnGuess()
    {
        // The message declared the two orders indistinguishable and then recommended
        // "-From utf-16", which resolves to the very reading it had just refused to
        // stand behind - and under a name that cannot express either choice.
        string diagnostic = BomlessUnicodeSafety.DescribeRefusal(
            new UnicodeEncoding(bigEndian: false, byteOrderMark: false));

        Assert.Contains("-From utf-16le", diagnostic, StringComparison.Ordinal);
        Assert.Contains("-From utf-16be", diagnostic, StringComparison.Ordinal);

        // The bare alias must not be offered as a resolution on its own.
        Assert.DoesNotContain("-From utf-16.", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("-From utf-16)", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void BothNamedByteOrdersAreOptionsEcActuallyAccepts()
    {
        // A message naming an encoding the CLI would reject is worse than no advice.
        foreach (string charset in new[] { "utf-16le", "utf-16be" })
        {
            var options = new Program.CliOptions
            {
                BasePath = _root,
                Target = "utf-8",
                From = charset,
            };

            Assert.True(Program.TryValidateOptions(options, out string? error), error);
        }
    }
}
