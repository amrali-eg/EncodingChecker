using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// An unusable report or journal destination must stop the run before files change.
/// </summary>
/// <remarks>
/// EC once found these failures after conversion, leaving changed files without the
/// requested record. Preflight changes when the failure is found, not its meaning:
/// <c>docs/CLI.md</c> and <see cref="ExitCodeContractTests"/> keep exit code 3.
/// </remarks>
public sealed class OutputDestinationPreflightTests : IDisposable
{
    private const int ExpectedProcessingErrors = 3;

    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_preflight_").FullName;

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

    private string WriteSource()
    {
        string path = Path.Combine(_root, "source.txt");
        File.WriteAllText(path, "hello world\n", new UTF8Encoding(false));
        return path;
    }

    /// <summary>An unwritable destination must leave every selected file unchanged.</summary>
    [Theory]
    [InlineData("-Journal")]
    [InlineData("-Report")]
    public void AnOutputUnderAMissingDirectoryLeavesTheSourceAlone(string option)
    {
        string source = WriteSource();
        byte[] before = File.ReadAllBytes(source);

        int exitCode = Run(
            "-BasePath", _root,
            "-Include", "source.txt",
            "-Target", "utf-16-bom",
            option, Path.Combine(_root, "no-such-folder", "output.json"));

        Assert.Equal(ExpectedProcessingErrors, exitCode);
        Assert.Equal(before, File.ReadAllBytes(source));
    }

    /// <summary>An existing directory is not a valid report-file destination.</summary>
    [Theory]
    [InlineData("-Journal")]
    [InlineData("-Report")]
    public void AnOutputOntoAnExistingDirectoryLeavesTheSourceAlone(string option)
    {
        string source = WriteSource();
        byte[] before = File.ReadAllBytes(source);

        string occupied = Path.Combine(_root, "occupied");
        Directory.CreateDirectory(occupied);

        int exitCode = Run(
            "-BasePath", _root,
            "-Include", "source.txt",
            "-Target", "utf-16-bom",
            option, occupied);

        Assert.Equal(ExpectedProcessingErrors, exitCode);
        Assert.Equal(before, File.ReadAllBytes(source));
    }

    /// <summary>A usable output path must pass preflight and allow the run.</summary>
    [Fact]
    public void AUsableDestinationStillRuns()
    {
        WriteSource();

        string report = Path.Combine(_root, "report.csv");

        int exitCode = Run(
            "-BasePath", _root,
            "-Include", "source.txt",
            "-DetectOnly",
            "-Report", report);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(report));
    }
}
