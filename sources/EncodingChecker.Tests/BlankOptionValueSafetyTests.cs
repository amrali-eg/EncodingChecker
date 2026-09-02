using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// An option that arrives with nothing in it must be a usage error, never a default.
/// </summary>
/// <remarks>
/// Every option is tested for presence with <c>IsNullOrWhiteSpace</c>, so a blank value
/// used to read as "not supplied" and fall through to whatever that option's absence
/// means. For two options that fall-through wrote files: <c>-Plan ""</c> skipped
/// <c>WhatIf</c> and performed the conversion its own flag exists to prevent, and
/// <c>-Apply ""</c> became a whole-directory conversion instead of a reviewed plan.
/// <para>
/// These run the real CLI entry point rather than the parser, because the property that
/// matters is not the error message but that nothing on disk changed.
/// </para>
/// </remarks>
public sealed class BlankOptionValueSafetyTests : IDisposable
{
    private const int ExpectedUsageError = 1;

    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_blankvalue_").FullName;

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

    /// <summary>A UTF-8 file with a BOM: converting it to utf-8 without one rewrites it.</summary>
    private string WriteConvertibleFile()
    {
        string path = Path.Combine(_root, "convertible.txt");
        File.WriteAllText(path, "hello world", new UTF8Encoding(true));

        return path;
    }

    [Theory]
    [InlineData("-Plan")]
    [InlineData("-Apply")]
    [InlineData("-From")]
    [InlineData("-Journal")]
    [InlineData("-Report")]
    [InlineData("-Include")]
    [InlineData("-Exclude")]
    public void BlankOptionValue_IsRejectedAndChangesNothing(string flag)
    {
        string path = WriteConvertibleFile();
        byte[] before = File.ReadAllBytes(path);

        int exitCode = Run("-BasePath", _root, "-Target", "utf-8", flag, "");

        Assert.Equal(ExpectedUsageError, exitCode);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(_root, "*.bak"));
    }

    [Fact]
    public void BlankPlanValue_DoesNotSilentlyBecomeARealConversion()
    {
        // The specific regression: -Plan is documented as writing a preview and
        // changing nothing, and the blank value removed exactly that guarantee. The
        // BOM assertion is what makes this fail loudly rather than just counting bytes.
        string path = WriteConvertibleFile();

        Assert.Equal(ExpectedUsageError, Run("-BasePath", _root, "-Target", "utf-8", "-Plan", ""));

        byte[] bytes = File.ReadAllBytes(path);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
    }

    [Fact]
    public void BlankApplyValue_DoesNotSilentlyBecomeADirectoryConversion()
    {
        string path = WriteConvertibleFile();

        Assert.Equal(ExpectedUsageError, Run("-BasePath", _root, "-Target", "utf-8", "-Apply", ""));

        byte[] bytes = File.ReadAllBytes(path);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
    }

    [Fact]
    public void BlankSourceValue_DoesNotSilentlyRevertToAutomaticDetection()
    {
        // -From "" fell through to detection, which is a different safety decision
        // rather than a missing one: legacy bytes would be refused instead of read
        // as the codec the caller believed they had named.
        string path = Path.Combine(_root, "legacy.txt");
        File.WriteAllBytes(path, Encoding.GetEncoding("windows-1252").GetBytes("café"));
        byte[] before = File.ReadAllBytes(path);

        Assert.Equal(ExpectedUsageError, Run("-BasePath", _root, "-Target", "utf-8", "-From", ""));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void BlankValueIsRejectedBeforeTheDirectoryIsEvenRead()
    {
        // Parsing fails first, so a blank value cannot depend on the scan for safety.
        Assert.Equal(
            ExpectedUsageError,
            Run("-BasePath", Path.Combine(_root, "no-such-folder"), "-Target", "utf-8", "-Plan", ""));
    }

    [Fact]
    public void RealValuesStillWork()
    {
        // The guard must reject blank values without rejecting ordinary ones.
        string path = WriteConvertibleFile();
        string plan = Path.Combine(_root, "plan.json");

        Assert.Equal(0, Run("-BasePath", _root, "-Target", "utf-8", "-Plan", plan, "-Quiet"));
        Assert.True(File.Exists(plan));
        Assert.Equal(Encoding.UTF8.GetPreamble(), File.ReadAllBytes(path)[..3]);
    }
}
