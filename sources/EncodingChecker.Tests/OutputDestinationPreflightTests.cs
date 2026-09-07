using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// An output destination that cannot be written must be refused before the run, not after.
/// </summary>
/// <remarks>
/// The journal and the report are written once scanning has returned, so a destination
/// that cannot exist used to be discovered only after conversion had rewritten the files.
/// A user who asked for a journal ended with changed files and no record of the change,
/// which is the one outcome the journal exists to prevent.
///
/// The exit code is deliberately unchanged. <c>docs/CLI.md</c> assigns 3 to a report
/// failure and <see cref="ExitCodeContractTests"/> pins it; finding the failure earlier
/// must change *when* it is reported, not *what* is reported.
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

    /// <summary>
    /// The defect itself: the bytes must survive a destination the run cannot write.
    /// </summary>
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

    /// <summary>The other shape of the same mistake: the path is an existing folder.</summary>
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

    /// <summary>
    /// A usable destination must still be accepted, or the guard would be indistinguishable
    /// from refusing every run that asks for output.
    /// </summary>
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
