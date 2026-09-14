namespace EncodingChecker.Tests;

/// <summary>
/// Pins the exact precedence and diagnostics of <c>-Apply</c>'s option-conflict check
/// (<c>Program.ApplyConflict</c>) before any refactor of its implementation. A plan
/// already records its own scope and conversion settings, so twelve other options may
/// not be combined with <c>-Apply</c>; this fixture proves both each individual
/// rejection and which one wins when several are set at once.
/// </summary>
public sealed class ApplyConflictPrecedenceTests : IDisposable
{
    private readonly string _planPath = Path.GetTempFileName();

    public void Dispose()
    {
        try
        {
            File.Delete(_planPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    private Program.CliOptions BaseOptions() => new() { ApplyPath = _planPath };

    private const string GenericSuffix =
        " cannot be combined with -Apply. A plan already records its scope and " +
        "conversion settings; re-run -Plan to change them.";

    private const string WhatIfMessage =
        "-WhatIf cannot be combined with -Apply. The saved plan is the preview; " +
        "applying it performs the reviewed writes.";

    /// <summary>ApplyConflict's exact detection order — the contract this fixture pins.</summary>
    private static readonly string[] OrderedFlags =
    [
        "-BasePath", "-Include", "-Exclude", "-Target", "-From", "-Backup",
        "-WhatIf", "-DetectOnly", "-Validate", "-Report", "-FailOnChanges", "-Verbose",
    ];

    private static string MessageFor(string flag) =>
        flag == "-WhatIf" ? WhatIfMessage : flag + GenericSuffix;

    public static IEnumerable<object[]> IndividualConflicts()
    {
        yield return ["-BasePath", "-BasePath" + GenericSuffix];
        yield return ["-Include", "-Include" + GenericSuffix];
        yield return ["-Exclude", "-Exclude" + GenericSuffix];
        yield return ["-Target", "-Target" + GenericSuffix];
        yield return ["-From", "-From" + GenericSuffix];
        yield return ["-Backup", "-Backup" + GenericSuffix];
        yield return ["-WhatIf", WhatIfMessage];
        yield return ["-DetectOnly", "-DetectOnly" + GenericSuffix];
        yield return ["-Validate", "-Validate" + GenericSuffix];
        yield return ["-Report", "-Report" + GenericSuffix];
        yield return ["-FailOnChanges", "-FailOnChanges" + GenericSuffix];
        yield return ["-Verbose", "-Verbose" + GenericSuffix];
    }

    private static void SetOption(Program.CliOptions options, string flag)
    {
        switch (flag)
        {
            case "-BasePath": options.BasePath = @"C:\Somewhere"; break;
            case "-Include": options.Include.Add("*.txt"); break;
            case "-Exclude": options.Exclude.Add("*.bak"); break;
            case "-Target": options.Target = "utf-8"; break;
            case "-From": options.From = "windows-1252"; break;
            case "-Backup": options.Backup = true; break;
            case "-WhatIf": options.WhatIf = true; break;
            case "-DetectOnly": options.DetectOnly = true; break;
            case "-Validate": options.ValidateCharsets = "utf-8"; break;
            case "-Report": options.ReportPath = "report.csv"; break;
            case "-FailOnChanges": options.FailOnChanges = true; break;
            case "-Verbose": options.Verbose = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(flag), flag, null);
        }
    }

    [Theory]
    [MemberData(nameof(IndividualConflicts))]
    public void EachOptionAloneIsRejectedWithItsOwnMessage(string flag, string expectedError)
    {
        Program.CliOptions options = BaseOptions();
        SetOption(options, flag);

        bool valid = Program.TryValidateOptions(options, out string? error);

        Assert.False(valid);
        Assert.Equal(expectedError, error);
    }

    [Fact]
    public void JournalMaxParallelismAndQuietRemainValidApplyTimeControls()
    {
        Program.CliOptions options = BaseOptions();
        options.JournalPath = "journal.json";
        options.MaxParallelism = 2;
        options.Quiet = true;

        Assert.True(Program.TryValidateOptions(options, out string? error), error);
    }

    // Pins today's first-match-wins order across every adjacent pair (and beyond) in
    // OrderedFlags; this order must survive any refactor of ApplyConflict's implementation.
    public static IEnumerable<object[]> PrecedenceFromEachPosition()
    {
        for (int i = 0; i < OrderedFlags.Length; i++)
            yield return [i];
    }

    [Theory]
    [MemberData(nameof(PrecedenceFromEachPosition))]
    public void WinningFlagIsTheEarliestOneSetRegardlessOfWhatFollows(int winningIndex)
    {
        Program.CliOptions options = BaseOptions();

        // Set the winning flag and every flag after it; only the earliest may win.
        for (int i = winningIndex; i < OrderedFlags.Length; i++)
            SetOption(options, OrderedFlags[i]);

        bool valid = Program.TryValidateOptions(options, out string? error);

        Assert.False(valid);
        Assert.Equal(MessageFor(OrderedFlags[winningIndex]), error);
    }
}
