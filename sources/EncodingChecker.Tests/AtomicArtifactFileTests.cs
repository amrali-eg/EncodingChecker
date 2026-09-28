using System.Diagnostics;
using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// A failed artifact write must preserve the previous complete file.
/// </summary>
/// <remarks>
/// An interrupted in-place write once left plans, journals, reports, and settings
/// incomplete. Losing a reviewed plan is especially unsafe because regenerating it creates
/// a different, unreviewed plan. These tests pin preservation, not the writer's mechanism.
/// </remarks>
public sealed class AtomicArtifactFileTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_atomic_").FullName;

    public void Dispose()
    {
        try
        {
            // A destination left read-only by a case would otherwise stop the folder being deleted.
            foreach (string file in Directory.GetFiles(_root))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private string Existing(string name, string content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private string[] TempArtifacts() =>
        Directory.GetFiles(_root, "*." + EncodingConverter.TempFileSuffix);

    [Fact]
    public void AFailedWriteLeavesThePreviousArtifactIntact()
    {
        string path = Existing("plan.json", "{\"reviewed\":true}");
        byte[] before = File.ReadAllBytes(path);

        string? error = AtomicArtifactFile.Write(
            path, _ => throw new InvalidOperationException("serialiser gave up"));

        Assert.NotNull(error);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(TempArtifacts());
    }

    /// <summary>An unexpected write failure must still preserve the previous version.</summary>
    [Fact]
    public void AnUnexpectedFailureAlsoLeavesThePreviousArtifactIntact()
    {
        string path = Existing("journal.json", "{\"runs\":1}");
        byte[] before = File.ReadAllBytes(path);

        Assert.Throws<FormatException>(
            () => AtomicArtifactFile.Write(path, _ => throw new FormatException()));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(TempArtifacts());
    }

    [Fact]
    public void ASuccessfulWriteReplacesTheArtifactAndLeavesNoTemporaryFile()
    {
        string path = Existing("report.csv", "old,content\n");

        string? error = AtomicArtifactFile.WriteText(
            path, "new,content\n", new UTF8Encoding(false));

        Assert.Null(error);
        Assert.Equal("new,content\n", File.ReadAllText(path));
        Assert.Empty(TempArtifacts());
    }

    [Fact]
    public void ADestinationThatDoesNotExistYetIsCreated()
    {
        string path = Path.Combine(_root, "fresh.json");

        string? error = AtomicArtifactFile.WriteText(
            path, "{}", new UTF8Encoding(false));

        Assert.Null(error);
        Assert.Equal("{}", File.ReadAllText(path));
        Assert.Empty(TempArtifacts());
    }

    /// <summary>Writing a report through a stream must preserve its UTF-8 BOM for Excel.</summary>
    [Fact]
    public void TheEncodingsPreambleIsStillWritten()
    {
        string path = Path.Combine(_root, "bom.csv");

        Assert.Null(AtomicArtifactFile.WriteText(
            path, "File,Encoding\n", ConversionReport.CsvFileEncoding));

        Assert.Equal(
            ConversionReport.CsvFileEncoding.GetPreamble(),
            File.ReadAllBytes(path).Take(3));
    }

    /// <summary>A leftover temporary artifact must use the suffix excluded from scans.</summary>
    [Fact]
    public void TheTemporaryFileUsesTheSuffixScansAlreadyExclude()
    {
        string path = Path.Combine(_root, "observed.json");
        string? observed = null;

        AtomicArtifactFile.Write(path, _ =>
            observed = TempArtifacts().SingleOrDefault());

        Assert.NotNull(observed);
        Assert.True(DirectoryTraversal.HasReservedArtifactSuffix(observed));
        Assert.Empty(TempArtifacts());
    }

    // Every artifact EC writes: a plain write, a plan, and a journal. The GUI's journal export
    // and the CLI's -Plan and -Journal save through the last two.
    public static TheoryData<string> Writers => ["write", "plan", "journal"];

    private string? WriteWith(string writer, string path) => writer switch
    {
        "write" => AtomicArtifactFile.Write(path, stream => stream.WriteByte((byte)'x')),
        "plan" => ConversionPlan.FromEntries(
                [], _root, "utf-8", targetHasBom: false, backupEnabled: false, explicitSource: null)
            .Save(path),
        "journal" => ConversionJournal.FromRun(
                [], _root, "utf-8", targetHasBom: false, backupEnabled: false,
                explicitSource: null, surface: "Test", DateTime.UtcNow)
            .Save(path),
        _ => throw new ArgumentOutOfRangeException(nameof(writer)),
    };

    [Theory]
    [MemberData(nameof(Writers))]
    public void AReadOnlyDestinationIsRefusedNotReplaced(string writer)
    {
        // Installing the staged file would clear the flag and replace the file, where a direct
        // write would have failed. The refusal keeps both the file and the flag.
        string path = Existing("artifact.out", "the previous artifact");
        byte[] before = File.ReadAllBytes(path);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        string? error = WriteWith(writer, path);

        Assert.NotNull(error);
        Assert.Contains("read-only", error);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
        Assert.Empty(TempArtifacts());
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void ALinkAsTheDestinationIsRefusedNotReplaced(string writer)
    {
        // A junction is the link a test can create without special privileges; a file symlink
        // carries the same ReparsePoint attribute the refusal checks.
        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);

        string link = Path.Combine(_root, "link");
        Assert.True(
            RunCmd($"mklink /J \"{link}\" \"{target}\"") && Directory.Exists(link),
            "The junction fixture could not be created.");

        try
        {
            string? error = WriteWith(writer, link);

            Assert.NotNull(error);
            Assert.Contains("is a link", error);
            Assert.True(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint));
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
            Assert.Empty(TempArtifacts());
        }
        finally
        {
            Assert.True(RunCmd($"rmdir \"{link}\""));
        }
    }

    private static bool RunCmd(string command)
    {
        using Process? process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c {command}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        if (process is null)
            return false;

        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);

            return false;
        }

        return process.ExitCode == 0;
    }
}
