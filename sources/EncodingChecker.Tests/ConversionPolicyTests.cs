using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// One policy engine, asked by every surface.
///
/// This exists because the GUI had quietly grown a second answer. Ambiguity was
/// classified only during a Convert-mode scan; the GUI scans in Detect mode and converts
/// the rows the user checks, so every entry arrived carrying the default "unambiguous"
/// and the refusal never fired. The tool converted, on the strength of whatever detection
/// returned, the exact files it tells CLI users it will not convert — and nothing failed,
/// because no test drove the GUI's sequence.
///
/// The lesson is the one the audit already taught once: a safety rule enforced at one
/// call site is a safety rule the next call site does not have. So the decision lives in
/// <see cref="ConversionPolicy"/>, and these pin that every route reaches it.
/// </summary>
public sealed class ConversionPolicyTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_policy_").FullName;

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

    private string Write(string name, string text, string charset)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, Encoding.GetEncoding(charset).GetBytes(text));
        return path;
    }

    /// <summary>Detect-mode scan then convert the rows: what the GUI's buttons do.</summary>
    private List<ConversionReportEntry> ViewThenConvert(
        string target = "utf-8", bool whatIf = false)
    {
        var scanned = new List<ConversionReportEntry>();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                IncludeSubdirectories = true,
                IncludePatterns = ["*"],
                Action = ScanAction.Detect,
            },
            scanned.Add,
            CancellationToken.None);

        var completed = new List<ConversionReportEntry>();

        ScanEngine.ConvertFiles(
            scanned,
            target,
            targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: whatIf,
            backup: false,
            completed.Add,
            CancellationToken.None);

        return completed;
    }

    [Fact]
    public void TheGuiSequenceRefusesWhatTheCliRefuses()
    {
        // The regression. Before the policy was extracted this converted the file.
        byte[] original =
            Encoding.GetEncoding("windows-1252").GetBytes("Le café était déjà prêt");
        string path = Path.Combine(_root, "ambiguous.txt");
        File.WriteAllBytes(path, original);

        ConversionReportEntry entry = Assert.Single(ViewThenConvert());

        Assert.Equal(PlannedAction.Refuse, entry.Action);
        Assert.Equal(ConversionRowResult.Error, entry.Result);
        Assert.Equal(AmbiguityClass.TextChanging, entry.Ambiguity);
        Assert.Contains("could not be determined uniquely", entry.Diagnostic);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void TheGuiSequenceStillConvertsWhatIsSafe()
    {
        // The other direction. A gate that refuses everything is not safe, it is broken.
        const string text = "こんにちは世界。日本語のテキストです。";
        string path = Write("jp.txt", text, "shift_jis");

        ConversionReportEntry entry = Assert.Single(ViewThenConvert());

        Assert.Equal(PlannedAction.Convert, entry.Action);
        Assert.Equal(ConversionRowResult.Converted, entry.Result);
        Assert.Equal(text, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void BothSurfacesReachTheSameDecisionForTheSameFile()
    {
        // Stated directly rather than inferred from two separately-asserted outcomes.
        Write("ambiguous.txt", "Le café était déjà prêt", "windows-1252");
        Write("jp.txt", "こんにちは世界。日本語のテキストです。", "shift_jis");
        Write("plain.txt", "just ascii here", "ascii");

        Dictionary<string, PlannedAction?> viaGui = ViewThenConvert(whatIf: true)
            .ToDictionary(e => Path.GetFileName(e.FilePath), e => e.Action);

        var viaCli = new List<ConversionReportEntry>();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                IncludeSubdirectories = true,
                IncludePatterns = ["*"],
                Action = ScanAction.Convert,
                TargetCharset = "utf-8",
                TargetWriteBom = false,
                WhatIf = true,
            },
            viaCli.Add,
            CancellationToken.None);

        Assert.Equal(3, viaGui.Count);

        foreach (ConversionReportEntry entry in viaCli)
            Assert.Equal(entry.Action, viaGui[Path.GetFileName(entry.FilePath)]);
    }

    [Fact]
    public void TheThreeClassificationsMapToTheirActions()
    {
        // Unambiguous converts; several codecs agreeing on the text converts, with the
        // ambiguity disclosed; several codecs disagreeing refuses until somebody chooses.
        // These three are the whole user-facing contract of the refusal.
        AssertMaps(AmbiguityClass.Unambiguous, PlannedAction.Convert, discloses: false);
        AssertMaps(AmbiguityClass.TextEquivalent, PlannedAction.Convert, discloses: true);
        AssertMaps(AmbiguityClass.TextChanging, PlannedAction.Refuse, discloses: false);
    }

    private static void AssertMaps(
        AmbiguityClass ambiguity, PlannedAction expected, bool discloses)
    {
        PlannedAction action = ConversionPolicy.Decide(
            "windows-1252", sourceHasBom: false,
            "utf-8", targetHasBom: false,
            ambiguity, ["iso-8859-1"], out string? reason);

        Assert.Equal(expected, action);

        // A refusal always says why; a conversion never needs to.
        Assert.Equal(expected == PlannedAction.Refuse, reason is not null);
        Assert.Equal(discloses, ConversionPolicy.NeedsDisclosure(ambiguity));
    }

    [Fact]
    public void AFileAlreadyInTheTargetEncodingIsNotRefusedForAmbiguity()
    {
        // Nothing is written, so there is no reading of it to get wrong. Refusing here
        // would report a danger that does not exist.
        Assert.Equal(
            PlannedAction.Unchanged,
            ConversionPolicy.Decide(
                "utf-8", sourceHasBom: false,
                "utf-8", targetHasBom: false,
                AmbiguityClass.TextChanging, ["iso-8859-1"], out _));
    }

    [Fact]
    public void AnUnidentifiedSourceIsSkippedByBothTheGuardAndThePolicy()
    {
        // ConvertFiles has to answer for an unidentified source before it can resolve an
        // Encoding, so it cannot reach the policy. The two must not drift apart.
        Assert.Equal(
            PlannedAction.Skip,
            ConversionPolicy.Decide(
                ScanEngine.UNKNOWN_CHARSET, sourceHasBom: false,
                "utf-8", targetHasBom: false,
                AmbiguityClass.Unambiguous, [], out _));

        var entry = new ConversionReportEntry
        {
            FilePath = Write("binary.bin", "irrelevant", "ascii"),
            SourceEncoding = ScanEngine.UNKNOWN_CHARSET,
            SourceHasBom = false,
            TargetEncoding = "utf-8",
            TargetHasBom = false,
        };

        var completed = new List<ConversionReportEntry>();

        ScanEngine.ConvertFiles(
            [entry], "utf-8", targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false, backup: false, completed.Add, CancellationToken.None);

        Assert.Equal(PlannedAction.Skip, Assert.Single(completed).Action);
    }

    [Fact]
    public void AnExplicitSourceSkipsClassificationButNotTheConversionSafeguards()
    {
        // Explicit source is an answer to "which encoding is this?", not permission to
        // convert regardless. EUC-JP bytes carrying a JIS X 0212 sequence still cannot
        // be decoded by code page 51932.
        byte[] unrepresentable =
            [0x8F, 0xB0, 0xDF, 0xB9, 0xA5, 0xA1, 0xA4, 0xC0, 0xA4, 0xB3];
        string path = Path.Combine(_root, "named.txt");
        File.WriteAllBytes(path, unrepresentable);

        var entry = new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = "euc-jp",
            SourceHasBom = false,
            TargetEncoding = "euc-jp",
            TargetHasBom = false,
            SourceEncodingWasSpecified = true,
        };

        var completed = new List<ConversionReportEntry>();

        ScanEngine.ConvertFiles(
            [entry], "utf-8", targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false, backup: false, completed.Add, CancellationToken.None);

        ConversionReportEntry result = Assert.Single(completed);

        // The policy let it through - naming the encoding settled the ambiguity - and
        // the conversion engine refused it anyway.
        Assert.Equal(PlannedAction.Convert, result.Action);
        Assert.Equal(ConversionRowResult.Error, result.Result);
        Assert.Equal(unrepresentable, File.ReadAllBytes(path));
    }

    /// <summary>
    /// What the confirmation dialog does when the user answers a refusal by naming the
    /// encoding: the same override <c>-From</c> uses, applied to the refused entries.
    /// </summary>
    private static void ChooseSourceEncoding(
        ConversionReportEntry entry, string charset)
    {
        entry.CurrentCharsetLabel = charset;
        entry.SourceEncodingWasSpecified = true;
        entry.Ambiguity = AmbiguityClass.Unambiguous;
        entry.AmbiguityReason = AmbiguityReason.ExplicitlySpecified;
        entry.CompetingEncodings = [];
        entry.Diagnostic = null;
        entry.Action = null;
    }

    [Fact]
    public void ChoosingTheSourceEncodingResolvesARefusalInTheGui()
    {
        // The refusal tells the user to say which encoding it is. Saying so has to work,
        // or the safety feature issues advice its own interface cannot take.
        byte[] bytes = Encoding.GetEncoding("windows-1252").GetBytes("Le café était prêt");
        string path = Path.Combine(_root, "ambiguous.txt");
        File.WriteAllBytes(path, bytes);

        ConversionReportEntry entry = Assert.Single(ViewThenConvert());
        Assert.Equal(PlannedAction.Refuse, entry.Action);

        ChooseSourceEncoding(entry, "windows-1252");

        var completed = new List<ConversionReportEntry>();

        ScanEngine.ConvertFiles(
            [entry], "utf-8", targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false, backup: false, completed.Add, CancellationToken.None);

        ConversionReportEntry result = Assert.Single(completed);

        Assert.Equal(PlannedAction.Convert, result.Action);
        Assert.Equal(ConversionRowResult.Converted, result.Result);
        Assert.Equal("Le café était prêt", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void TheChosenEncodingIsWhatTheConversionActuallyUses()
    {
        // Not just permission to proceed. Naming a different encoding for the same bytes
        // has to produce different text, or the choice is decoration.
        byte[] bytes = Encoding.GetEncoding("windows-1252").GetBytes("café");
        string path = Path.Combine(_root, "interpretation.txt");
        File.WriteAllBytes(path, bytes);

        ConversionReportEntry entry = Assert.Single(ViewThenConvert());
        ChooseSourceEncoding(entry, "koi8-r");

        var completed = new List<ConversionReportEntry>();

        ScanEngine.ConvertFiles(
            [entry], "utf-8", targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false, backup: false, completed.Add, CancellationToken.None);

        Assert.Equal(ConversionRowResult.Converted, Assert.Single(completed).Result);

        string text = Encoding.UTF8.GetString(File.ReadAllBytes(path));

        Assert.NotEqual("café", text);
        Assert.Equal(Encoding.GetEncoding("koi8-r").GetString(bytes), text);
    }

    [Fact]
    public void APlanShowsTheChosenEncodingRatherThanTheDetectedOne()
    {
        // The confirmation is read after the choice is made. It has to describe the
        // conversion that will happen, not the one that was refused.
        File.WriteAllBytes(
            Path.Combine(_root, "ambiguous.txt"),
            Encoding.GetEncoding("windows-1252").GetBytes("Le café était prêt"));

        ConversionReportEntry entry = Assert.Single(ViewThenConvert(whatIf: true));
        Assert.NotEqual("koi8-r", entry.SourceEncoding);

        ChooseSourceEncoding(entry, "koi8-r");

        ScanEngine.ConvertFiles(
            [entry], "utf-8", targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: true, backup: false, _ => { }, CancellationToken.None);

        ConversionPlan plan = ConversionPlan.FromEntries(
            [entry], _root, "utf-8", targetHasBom: false,
            backupEnabled: false, explicitSource: "koi8-r");

        PlannedFile planned = Assert.Single(plan.Files);

        Assert.Equal("koi8-r", planned.SourceEncoding);
        Assert.Equal(PlannedAction.Convert, planned.Action);
        Assert.True(planned.SourceWasSpecified);
        Assert.False(planned.MayChangeText);
    }

    [Fact]
    public void ChoosingAnEncodingTheBytesCannotBeIsStillRefused()
    {
        // Explicit source ends the ambiguity question, not the conversion safeguards.
        // These EUC-JP bytes carry a JIS X 0212 sequence code page 51932 cannot map.
        byte[] unrepresentable =
            [0x8F, 0xB0, 0xDF, 0xB9, 0xA5, 0xA1, 0xA4, 0xC0, 0xA4, 0xB3];
        string path = Path.Combine(_root, "undecodable.txt");
        File.WriteAllBytes(path, unrepresentable);

        List<ConversionReportEntry> scanned = ViewThenConvert(whatIf: true);
        ConversionReportEntry entry = Assert.Single(scanned);

        ChooseSourceEncoding(entry, "euc-jp");

        var completed = new List<ConversionReportEntry>();

        ScanEngine.ConvertFiles(
            [entry], "utf-8", targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false, backup: false, completed.Add, CancellationToken.None);

        Assert.Equal(ConversionRowResult.Error, Assert.Single(completed).Result);
        Assert.Equal(unrepresentable, File.ReadAllBytes(path));
    }

    [Fact]
    public void ChoosingAnEncodingDoesNotSkipTheBackupRequirement()
    {
        byte[] original = Encoding.GetEncoding("windows-1252").GetBytes("café");
        string path = Path.Combine(_root, "backupfail.txt");
        File.WriteAllBytes(path, original);

        ConversionReportEntry entry = Assert.Single(ViewThenConvert(whatIf: true));
        ChooseSourceEncoding(entry, "windows-1252");

        // A directory where the .bak has to go: the copy cannot succeed.
        Directory.CreateDirectory(path + ".bak");

        var completed = new List<ConversionReportEntry>();

        ScanEngine.ConvertFiles(
            [entry], "utf-8", targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false, backup: true, completed.Add, CancellationToken.None);

        Assert.Equal(ConversionRowResult.Error, Assert.Single(completed).Result);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void AnEntryNobodyDecidedOnCannotReachAPlan()
    {
        // The shape of the bug this class exists for: an entry that never went through
        // the policy must not be planned as a conversion by default.
        var undecided = new ConversionReportEntry
        {
            FilePath = Write("undecided.txt", "text", "ascii"),
            SourceEncoding = "us-ascii",
            SourceHasBom = false,
            TargetEncoding = "utf-8",
            TargetHasBom = false,
        };

        Assert.Null(undecided.Action);

        Assert.Throws<InvalidOperationException>(() => ConversionPlan.FromEntries(
            [undecided], _root, "utf-8", targetHasBom: false,
            backupEnabled: false, explicitSource: null));
    }
}
