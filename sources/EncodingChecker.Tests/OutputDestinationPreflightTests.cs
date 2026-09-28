using System.Text;
using static EncodingChecker.Tests.CliRunner;
using static EncodingChecker.Tests.ExpectedExitCode;

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

    /// <summary>
    /// A read-only output would be refused when written, so it must be refused before any
    /// file changes, and keep its own bytes and flag.
    /// </summary>
    [Theory]
    [InlineData("-Journal")]
    [InlineData("-Report")]
    [InlineData("-Plan")]
    public void AReadOnlyOutputLeavesTheSourceAndTheOutputAlone(string option)
    {
        string source = WriteSource();
        byte[] before = File.ReadAllBytes(source);

        string output = Path.Combine(_root, "output.out");
        File.WriteAllText(output, "the previous output");
        byte[] outputBefore = File.ReadAllBytes(output);
        File.SetAttributes(output, FileAttributes.ReadOnly);

        try
        {
            int exitCode = Run(
                out string stderr,
                "-BasePath", _root,
                "-Include", "source.txt",
                "-Target", "utf-16-bom",
                option, output);

            Assert.Equal(ExpectedProcessingErrors, exitCode);
            Assert.Contains($"{option}: '{output}' is read-only.", stderr);
            Assert.Equal(before, File.ReadAllBytes(source));
            Assert.Equal(outputBefore, File.ReadAllBytes(output));
            Assert.True(File.GetAttributes(output).HasFlag(FileAttributes.ReadOnly));
        }
        finally
        {
            File.SetAttributes(output, FileAttributes.Normal);
        }
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
