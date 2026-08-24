namespace EncodingChecker.Tests;

/// <summary>
/// Two places where the CLI used to discard what the caller typed without saying so:
/// -Validate silently won over -Target, and a repeated -Include silently dropped every
/// pattern but the last. Both are the same failure mode - the run proceeds, the user is
/// told nothing, and the result does not reflect the command they wrote.
/// </summary>
public sealed class CliInputPreservationTests
{
    private static Program.CliOptions Parse(params string[] args)
    {
        Assert.True(
            Program.TryParseArguments(args, out Program.CliOptions options, out string? parseError),
            parseError);

        return options;
    }

    // ---------- -Validate + -Target ----------

    [Fact]
    public void ValidateCombinedWithTarget_IsRejected()
    {
        Program.CliOptions options = Parse(
            "-BasePath", ".", "-Validate", "utf-8", "-Target", "utf-16");

        Assert.False(Program.TryValidateOptions(options, out string? error));
        Assert.NotNull(error);
        Assert.Contains("-Validate", error, StringComparison.Ordinal);
        Assert.Contains("-Target", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateWithoutTarget_StillValidates()
    {
        Program.CliOptions options = Parse("-BasePath", ".", "-Validate", "utf-8,utf-8-bom");

        Assert.True(Program.TryValidateOptions(options, out string? error), error);
    }

    [Fact]
    public void TargetWithoutValidate_StillValidates()
    {
        Program.CliOptions options = Parse("-BasePath", ".", "-Target", "utf-8");

        Assert.True(Program.TryValidateOptions(options, out string? error), error);
    }

    [Fact]
    public void DetectOnlyCombinedWithValidate_IsStillRejected()
    {
        // The pre-existing incompatibility this one was modelled on.
        Program.CliOptions options = Parse(
            "-BasePath", ".", "-DetectOnly", "-Validate", "utf-8");

        Assert.False(Program.TryValidateOptions(options, out string? error));
        Assert.NotNull(error);
    }

    // ---------- repeated -Include / -Exclude ----------

    [Fact]
    public void RepeatedInclude_KeepsEveryPattern()
    {
        Program.CliOptions options = Parse(
            "-BasePath", ".", "-Include", "*.cs", "-Include", "*.txt", "-DetectOnly");

        Assert.Equal(["*.cs", "*.txt"], options.Include);
    }

    [Fact]
    public void RepeatedExclude_KeepsEveryPattern()
    {
        Program.CliOptions options = Parse(
            "-BasePath", ".", "-Exclude", "*.g.cs", "-Exclude", "*.designer.cs", "-DetectOnly");

        Assert.Equal(["*.g.cs", "*.designer.cs"], options.Exclude);
    }

    [Fact]
    public void CommaSeparatedPatterns_BehaveExactlyAsBefore()
    {
        Program.CliOptions options = Parse(
            "-BasePath", ".", "-Include", "*.cs,*.txt,*.md", "-DetectOnly");

        Assert.Equal(["*.cs", "*.txt", "*.md"], options.Include);
    }

    [Fact]
    public void SinglePattern_IsUnchanged()
    {
        Program.CliOptions options = Parse("-BasePath", ".", "-Include", "*.cs", "-DetectOnly");

        Assert.Equal(["*.cs"], options.Include);
    }

    [Fact]
    public void RepeatedAndCommaSeparatedTogether_ProduceTheCombinedSet()
    {
        Program.CliOptions options = Parse(
            "-BasePath", ".",
            "-Include", "*.cs,*.vb",
            "-Include", "*.txt",
            "-Exclude", "*.g.cs",
            "-Exclude", "bin/*,obj/*",
            "-DetectOnly");

        Assert.Equal(["*.cs", "*.vb", "*.txt"], options.Include);
        Assert.Equal(["*.g.cs", "bin/*", "obj/*"], options.Exclude);
    }

    [Fact]
    public void NoIncludeGiven_LeavesTheListEmpty()
    {
        // An empty list means "match everything" downstream; accumulation must not
        // invent a pattern where none was supplied.
        Program.CliOptions options = Parse("-BasePath", ".", "-DetectOnly");

        Assert.Empty(options.Include);
        Assert.Empty(options.Exclude);
    }
}
