using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// EncodingChecker's exit codes are a published CLI contract - documented in the
/// README and printed by -?. Codes 0-4 are deliberately shared with
/// LineEndingNormalizer; EC reserves 5 for a safe conversion refusal.
///
/// These pin the numbers themselves rather than just "non-zero". Renumbering them
/// would silently change the meaning of results already relied on by CI gates
/// (exit 2 = -FailOnChanges is the most likely to be scripted), so it must never
/// happen without an explicit, deliberate change here.
///
/// RunConsoleMode writes to Console.Out/Error, which is process-global, so these
/// redirect around each call.
/// </summary>
public sealed class ExitCodeContractTests : IDisposable
{
    private const int ExpectedClean = 0;
    private const int ExpectedUsageError = 1;
    private const int ExpectedChangesNeeded = 2;
    private const int ExpectedProcessingErrors = 3;
    private const int ExpectedSafeRefusal = 5;

    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_exitcodes_").FullName;

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

    private string WriteAscii(string name, string content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content, Encoding.ASCII);

        return path;
    }

    [Fact]
    public void Help_ExitsZero()
    {
        Assert.Equal(ExpectedClean, Run("-?"));
    }

    [Fact]
    public void CleanDetectOnlyRun_ExitsZero()
    {
        WriteAscii("a.txt", "hello");

        Assert.Equal(
            ExpectedClean,
            Run("-BasePath", _root, "-Include", "*.txt", "-DetectOnly"));
    }

    [Fact]
    public void UnknownArgument_ExitsOne()
    {
        Assert.Equal(ExpectedUsageError, Run("-BasePath", _root, "-NoSuchSwitch"));
    }

    [Fact]
    public void MissingBasePath_ExitsOne()
    {
        Assert.Equal(ExpectedUsageError, Run("-Include", "*.txt", "-DetectOnly"));
    }

    [Fact]
    public void NonexistentBasePath_ExitsOne()
    {
        // EncodingChecker folds "directory not found" into the usage-error code.
        // LineEndingNormalizer reports this as 5 instead - a refinement of the same
        // case, deliberately at a number EncodingChecker never returns.
        Assert.Equal(
            ExpectedUsageError,
            Run("-BasePath", Path.Combine(_root, "nope"), "-Include", "*", "-DetectOnly"));
    }

    [Fact]
    public void ConvertModeWithoutTarget_ExitsOne()
    {
        Assert.Equal(ExpectedUsageError, Run("-BasePath", _root, "-Include", "*.txt"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnsupportedUtf7ConversionCodec_ExitsOneWithoutCrashing(bool useAsSource)
    {
        string[] args = useAsSource
            ? ["-BasePath", _root, "-Include", "*.txt", "-From", "utf-7", "-Target", "utf-8"]
            : ["-BasePath", _root, "-Include", "*.txt", "-Target", "utf-7"];

        Assert.Equal(
            ExpectedUsageError,
            Run(args));
    }

    [Fact]
    public void FailOnChanges_WithFilesNeedingConversion_ExitsTwo()
    {
        // The CI-gate code, and the one most likely to be scripted: it must stay 2,
        // matching LineEndingNormalizer's -FailOnChanges.
        WriteAscii("a.txt", "hello");

        Assert.Equal(
            ExpectedChangesNeeded,
            Run(
                "-BasePath", _root,
                "-Include", "*.txt",
                "-Target", "utf-8-bom",
                "-WhatIf",
                "-FailOnChanges"));
    }

    [Fact]
    public void FailOnChanges_WithNothingToConvert_ExitsZero()
    {
        string path = Path.Combine(_root, "already.txt");
        File.WriteAllText(path, "hello", new UTF8Encoding(true));

        Assert.Equal(
            ExpectedClean,
            Run(
                "-BasePath", _root,
                "-Include", "already.txt",
                "-Target", "utf-8-bom",
                "-FailOnChanges"));
    }

    [Fact]
    public void UnwritableReportPath_ExitsThree()
    {
        // A report that cannot be written is a processing failure, not a usage error:
        // in Convert mode the files have already been rewritten by this point.
        WriteAscii("a.txt", "hello");

        string reportPath = Path.Combine(_root, "report.csv");
        Directory.CreateDirectory(reportPath);

        Assert.Equal(
            ExpectedProcessingErrors,
            Run(
                "-BasePath", _root,
                "-Include", "*.txt",
                "-DetectOnly",
                "-Report", reportPath));
    }

    [Fact]
    public void PerFileConversionFailure_ExitsThree()
    {
        // Cyrillic cannot be represented in windows-1252, so the file fails to convert.
        string path = Path.Combine(_root, "cyrillic.txt");
        File.WriteAllText(path, "Привет", new UTF8Encoding(false));

        Assert.Equal(
            ExpectedProcessingErrors,
            Run(
                "-BasePath", _root,
                "-Include", "cyrillic.txt",
                "-Target", "windows-1252"));
    }

    [Fact]
    public void RefusalOnlyRun_ExitsFive()
    {
        string path = Path.Combine(_root, "legacy.txt");
        File.WriteAllBytes(
            path,
            Encoding.GetEncoding("windows-1252").GetBytes("Le café était prêt"));

        Assert.Equal(
            ExpectedSafeRefusal,
            Run(
                "-BasePath", _root,
                "-Include", "legacy.txt",
                "-Target", "utf-8"));
    }

    [Fact]
    public void ProcessingFailure_IsNotReportedAsAUsageError()
    {
        // These must stay distinct: a CI gate has to tell "you invoked me wrong" from
        // "the run started and some files failed".
        Assert.NotEqual(ExpectedUsageError, ExpectedProcessingErrors);
        Assert.NotEqual(ExpectedSafeRefusal, ExpectedProcessingErrors);
    }
}
