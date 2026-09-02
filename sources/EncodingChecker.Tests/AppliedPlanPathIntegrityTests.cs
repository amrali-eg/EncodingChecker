using System.Diagnostics;
using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// A plan names paths, and a path is only as stable as the directories above it.
/// </summary>
/// <remarks>
/// Planning refuses a reparse-point <c>-BasePath</c> and traversal never enters one, so
/// no planned path can legitimately contain one. Applying a plan checked only that the
/// recorded directory still existed, so the same input was rejected by one entry point
/// and followed by the other: rename the root away, put a junction in its place, and the
/// writes land in a tree the reviewer never saw.
/// <para>
/// The recorded hashes cannot detect this. Two identical copies hash identically, which
/// is the same limit as BOM-less UTF-16 in a different guise - bytes cannot say which
/// file they are.
/// </para>
/// <para>
/// The junction tests return early where <c>mklink</c> is unavailable, matching
/// <see cref="ReparsePointRootTests"/>. The two tests below them need no junction and so
/// run everywhere, which is what keeps this fixture from passing vacuously.
/// </para>
/// </remarks>
public sealed class AppliedPlanPathIntegrityTests : IDisposable
{
    private const int ExpectedClean = 0;
    private const int ExpectedProcessingErrors = 3;

    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_planpath_").FullName;

    public void Dispose()
    {
        foreach (string dir in new[] { Path.Combine(_root, "real", "sub"), Path.Combine(_root, "real") })
        {
            if (Directory.Exists(dir) &&
                (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
            {
                RunCmd($"rmdir \"{dir}\"");
            }
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private static bool RunCmd(string arguments)
    {
        var psi = new ProcessStartInfo("cmd.exe", "/c " + arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using Process? proc = Process.Start(psi);

        if (proc is null)
            return false;

        proc.WaitForExit(10000);

        return proc.ExitCode == 0;
    }

    /// <summary>Creates a junction, or returns false where the environment forbids it.</summary>
    private static bool TryCreateJunction(string link, string target) =>
        RunCmd($"mklink /J \"{link}\" \"{target}\"") && Directory.Exists(link);

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

    private static void WriteWithBom(string path) =>
        File.WriteAllText(path, "identical bytes", new UTF8Encoding(true));

    private static bool StillHasBom(string path) =>
        File.ReadAllBytes(path).Take(3).SequenceEqual(Encoding.UTF8.GetPreamble());

    [Fact]
    public void PlanRootReplacedByAJunction_IsRefusedRatherThanFollowed()
    {
        string real = Path.Combine(_root, "real");
        string other = Path.Combine(_root, "other");
        Directory.CreateDirectory(real);
        Directory.CreateDirectory(other);
        WriteWithBom(Path.Combine(real, "f.txt"));
        WriteWithBom(Path.Combine(other, "f.txt"));

        string planPath = Path.Combine(_root, "plan.json");

        Assert.Equal(
            ExpectedClean,
            Run("-BasePath", real, "-Target", "utf-8", "-Plan", planPath, "-Quiet"));

        Directory.Move(real, Path.Combine(_root, "real_orig"));

        if (!TryCreateJunction(real, other))
            return; // mklink unavailable here; nothing to verify.

        Assert.Equal(ExpectedProcessingErrors, Run("-Apply", planPath));

        // Neither tree was written to: not the copy behind the junction, and not the
        // planned tree either, which no longer sits where the plan says it does.
        Assert.True(StillHasBom(Path.Combine(other, "f.txt")));
        Assert.True(StillHasBom(Path.Combine(_root, "real_orig", "f.txt")));
    }

    [Fact]
    public void SubdirectoryReplacedByAJunction_IsRefusedEvenThoughTheRootIsReal()
    {
        // The root check alone is not enough: only one directory along the path has to
        // change for the writes to leave the reviewed tree.
        string real = Path.Combine(_root, "real");
        string sub = Path.Combine(real, "sub");
        string elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(sub);
        Directory.CreateDirectory(elsewhere);
        WriteWithBom(Path.Combine(real, "f.txt"));
        WriteWithBom(Path.Combine(sub, "g.txt"));
        WriteWithBom(Path.Combine(elsewhere, "g.txt"));

        string planPath = Path.Combine(_root, "plan.json");

        Assert.Equal(
            ExpectedClean,
            Run("-BasePath", real, "-Target", "utf-8", "-Plan", planPath, "-Quiet"));

        Directory.Delete(sub, recursive: true);

        if (!TryCreateJunction(sub, elsewhere))
            return; // mklink unavailable here; nothing to verify.

        Assert.Equal(ExpectedProcessingErrors, Run("-Apply", planPath));

        Assert.True(StillHasBom(Path.Combine(elsewhere, "g.txt")));

        // The plan was reviewed as a whole, so the file whose path is still sound is
        // left alone too rather than half-applying it.
        Assert.True(StillHasBom(Path.Combine(real, "f.txt")));
    }

    [Fact]
    public void HasReparsePointInPath_ChecksTheFinalPathComponent()
    {
        string real = Path.Combine(_root, "real");
        string elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(real);
        Directory.CreateDirectory(elsewhere);

        string link = Path.Combine(real, "planned-entry");
        Assert.True(
            TryCreateJunction(link, elsewhere),
            "The junction fixture could not be created.");

        try
        {
            // This checks the component passed as 'path', not merely its parent chain.
            // File and directory links expose the same ReparsePoint attribute.
            Assert.True(DirectoryTraversal.HasReparsePointInPath(real, link));
        }
        finally
        {
            Assert.True(RunCmd($"rmdir \"{link}\""));
        }
    }

    [Fact]
    public void AnOrdinaryNestedPlanStillApplies()
    {
        // The control, and it needs no junction, so it runs in every environment. A
        // check that only ever refuses would pass both tests above while having broken
        // -Apply outright.
        string real = Path.Combine(_root, "real");
        string sub = Path.Combine(real, "deep", "nested");
        Directory.CreateDirectory(sub);
        WriteWithBom(Path.Combine(real, "f.txt"));
        WriteWithBom(Path.Combine(sub, "g.txt"));

        string planPath = Path.Combine(_root, "plan.json");

        Assert.Equal(
            ExpectedClean,
            Run("-BasePath", real, "-Target", "utf-8", "-Plan", planPath, "-Quiet"));
        Assert.Equal(ExpectedClean, Run("-Apply", planPath));

        Assert.False(StillHasBom(Path.Combine(real, "f.txt")));
        Assert.False(StillHasBom(Path.Combine(sub, "g.txt")));
    }

    [Fact]
    public void HasReparsePointInPath_OrdinaryNesting_IsFalseAndStopsAtTheRoot()
    {
        string real = Path.Combine(_root, "real");
        string deep = Path.Combine(real, "a", "b", "c");
        Directory.CreateDirectory(deep);
        string file = Path.Combine(deep, "f.txt");
        File.WriteAllText(file, "x");

        Assert.False(DirectoryTraversal.HasReparsePointInPath(real, file));

        // Walking must stop at the root rather than continuing up into directories the
        // plan says nothing about - on some machines the temp path itself sits behind
        // a link, which would otherwise reject every plan.
        Assert.False(DirectoryTraversal.HasReparsePointInPath(deep, file));
    }

    [Fact]
    public void HasReparsePointInPath_PathOutsideRoot_FailsClosed()
    {
        string root = Path.Combine(_root, "root");
        string outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        string file = Path.Combine(outside, "f.txt");
        File.WriteAllText(file, "x");

        Assert.True(DirectoryTraversal.HasReparsePointInPath(root, file));
    }
}
