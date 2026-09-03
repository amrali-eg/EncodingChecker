using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EncodingChecker.GuiSmoke;

internal sealed record SmokePhaseResult(
    string Id,
    string Name,
    bool Passed,
    string? Error,
    IReadOnlyDictionary<string, string> Before,
    IReadOnlyDictionary<string, string> After);

internal sealed record SmokeReport
{
    public int ReportVersion { get; init; } = 1;
    public required string StartedUtc { get; init; }
    public required string CompletedUtc { get; init; }
    public string EcVersion { get; init; } = "unknown";
    public required string EcExecutable { get; init; }
    public required string EcSha256 { get; init; }
    public string? EcManagedAssembly { get; init; }
    public string? EcManagedAssemblySha256 { get; init; }
    public required string OS { get; init; }
    public required string DotNet { get; init; }
    public required string Workspace { get; init; }
    public required bool Passed { get; init; }
    public required IReadOnlyList<SmokePhaseResult> Phases { get; init; }
}

/// <summary>Creates fixtures, drives the GUI, and verifies the resulting files.</summary>
internal sealed class SmokeSuite
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly string _app;
    private readonly string _workspace;
    private readonly List<SmokePhaseResult> _results = [];

    internal SmokeSuite(string app, string workspace)
    {
        _app = app;
        _workspace = workspace;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    internal SmokeReport Run(string? onlyPhase = null)
    {
        string started = DateTime.UtcNow.ToString("O");

        RunIf("A", "Review cancellation changes nothing", PhaseA);
        RunIf("B", "Unicode and ASCII convert automatically", PhaseB);
        RunIf("C", "Explicit legacy source is scoped to selected files", PhaseC);
        RunIf("D", "Ambiguous BOM-less UTF-16 is refused", PhaseD);
        RunIf("E", "Explicit BOM-less UTF-16 converts safely", PhaseE);
        RunIf("F", "A stale reviewed file stops the whole run", PhaseF);
        RunIf("G", "Backup failure leaves the source unchanged", PhaseG);

        return new SmokeReport
        {
            StartedUtc = started,
            CompletedUtc = DateTime.UtcNow.ToString("O"),
            EcExecutable = _app,
            EcSha256 = Hash(_app),
            OS = Environment.OSVersion.VersionString,
            DotNet = Environment.Version.ToString(),
            Workspace = _workspace,
            Passed = _results.All(result => result.Passed),
            Phases = _results,
        };

        void RunIf(string id, string name, Action<PhaseContext> body)
        {
            if (onlyPhase is null || onlyPhase.Equals(id, StringComparison.OrdinalIgnoreCase))
                RunPhase(id, name, body);
        }
    }

    private void RunPhase(
        string id,
        string name,
        Action<PhaseContext> body)
    {
        string directory = Path.Combine(_workspace, id);
        Directory.CreateDirectory(directory);
        var phase = new PhaseContext(directory);
        string? error = null;

        try
        {
            body(phase);
        }
        catch (Exception ex)
        {
            error = ex.ToString();
        }

        Dictionary<string, string> after = Snapshot(directory);
        bool passed = error is null;
        _results.Add(new SmokePhaseResult(id, name, passed, error, phase.Before, after));

        Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {id}: {name}");

        if (!passed)
            Console.Error.WriteLine(error);
    }

    private void PhaseA(PhaseContext phase)
    {
        string directory = phase.Directory;
        Write(directory, "unicode.txt", "Hello, 世界 — Привет", StrictUtf8);
        Write(directory, "jp.txt", "こんにちは世界。日本語のテキストです。", CodePage("shift_jis"));
        Write(directory, "french.txt", "Prix: 100€ pour le café", CodePage("windows-1252"));
        Write(directory, "russian.txt", "Привет мир, это русский текст", CodePage("koi8-r"));
        Write(directory, "plain.txt", "plain ascii, no high bytes", Encoding.ASCII);

        Dictionary<string, string> before = phase.CaptureBefore();

        using var gui = new EcGuiDriver(_app);
        System.Windows.Automation.AutomationElement review = gui.OpenReview(directory, 5);
        Check(gui.ReviewContainsControl(review, "lstSourceEncoding"),
            "The mixed review did not offer a source-encoding choice.");
        gui.CancelReview(review);

        AssertSameFiles(before, Snapshot(directory));
        AssertNoArtifacts(directory);
    }

    private void PhaseB(PhaseContext phase)
    {
        string directory = phase.Directory;
        const string unicode = "Hello, 世界 — Привет";
        const string plain = "plain ascii, no high bytes";
        Write(directory, "unicode.txt", unicode, Encoding.Unicode, writeBom: true);
        Write(directory, "plain.txt", plain, Encoding.ASCII);

        Dictionary<string, string> before = phase.CaptureBefore();

        using var gui = new EcGuiDriver(_app);
        System.Windows.Automation.AutomationElement review = gui.OpenReview(directory, 2);
        Check(!gui.ReviewContainsControl(review, "lstSourceEncoding"),
            "An automatically safe batch unexpectedly requested a source encoding.");
        gui.Proceed(review);

        AssertUtf8(Path.Combine(directory, "unicode.txt"), unicode);
        AssertUtf8(Path.Combine(directory, "plain.txt"), plain);
        AssertRecovery(
            Path.Combine(directory, "unicode.txt"),
            before["unicode.txt"],
            sourceMode: "Detected",
            sourceCodePage: 1200);
    }

    private void PhaseC(PhaseContext phase)
    {
        string directory = phase.Directory;
        const string french = "Prix: 100€ pour le café était déjà prêt";
        const string russian = "Привет мир, это русский текст";
        Encoding windows1252 = CodePage("windows-1252");
        byte[] frenchBytes = windows1252.GetBytes(french);

        File.WriteAllBytes(Path.Combine(directory, "french.txt"), frenchBytes);
        Write(directory, "russian.txt", russian, CodePage("koi8-r"));

        Dictionary<string, string> before = phase.CaptureBefore();

        using var gui = new EcGuiDriver(_app);
        System.Windows.Automation.AutomationElement review = gui.OpenReview(directory, 2);
        review = gui.ConfirmSource(review, "iso-8859-1", "russian.txt");
        gui.Proceed(review);

        string explicitlyChosenText = Encoding.Latin1.GetString(frenchBytes);
        AssertUtf8(Path.Combine(directory, "french.txt"), explicitlyChosenText);
        Check(Hash(Path.Combine(directory, "russian.txt")) == before["russian.txt"],
            "The unselected legacy file changed.");
        Check(!File.Exists(Path.Combine(directory, "russian.txt.bak")),
            "The unselected legacy file received a backup.");
        AssertRecovery(
            Path.Combine(directory, "french.txt"),
            before["french.txt"],
            sourceMode: "Explicit",
            sourceCodePage: 28591);
    }

    private void PhaseD(PhaseContext phase)
    {
        string directory = phase.Directory;
        const string text = "\u4100\u0a00\u4200\u4100\u0a00\u4200";
        Write(directory, "ambiguous-utf16be.txt", text, Encoding.BigEndianUnicode);

        Dictionary<string, string> before = phase.CaptureBefore();

        using var gui = new EcGuiDriver(_app);
        System.Windows.Automation.AutomationElement review = gui.OpenReview(directory, 1);
        Check(gui.ReviewContainsControl(review, "lstSourceEncoding"),
            "The BOM-less UTF-16 refusal did not offer an explicit source choice.");
        gui.CancelReview(review);

        AssertSameFiles(before, Snapshot(directory));
        AssertNoArtifacts(directory);
    }

    private void PhaseE(PhaseContext phase)
    {
        string directory = phase.Directory;
        const string text = "\u4100\u0a00\u4200\u4100\u0a00\u4200";
        Write(directory, "ambiguous-utf16be.txt", text, Encoding.BigEndianUnicode);

        Dictionary<string, string> before = phase.CaptureBefore();

        using var gui = new EcGuiDriver(_app);
        System.Windows.Automation.AutomationElement review = gui.OpenReview(directory, 1);
        review = gui.ConfirmSource(review, "utf-16BE");
        gui.Proceed(review);

        string path = Path.Combine(directory, "ambiguous-utf16be.txt");
        AssertUtf8(path, text);
        AssertRecovery(
            path,
            before["ambiguous-utf16be.txt"],
            sourceMode: "Explicit",
            sourceCodePage: 1201);
    }

    private void PhaseF(PhaseContext phase)
    {
        string directory = phase.Directory;
        const string text = "This source changes after the review opens.";
        string path = Path.Combine(directory, "moving.txt");
        Write(directory, "moving.txt", text, Encoding.Unicode, writeBom: true);
        Write(
            directory,
            "stable.txt",
            "This source must remain untouched when its neighbour changes.",
            Encoding.Unicode,
            writeBom: true);

        Dictionary<string, string> before = phase.CaptureBefore();

        using var gui = new EcGuiDriver(_app);
        System.Windows.Automation.AutomationElement review = gui.OpenReview(directory, 2);

        using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            stream.Write(Encoding.Unicode.GetBytes(" changed"));

        string changedHash = Hash(path);
        gui.ProceedExpectingWarning(review);

        Check(Hash(path) == changedHash,
            "The stale source was modified after EC detected the changed hash.");
        Check(Hash(Path.Combine(directory, "stable.txt")) == before["stable.txt"],
            "A stable source was converted even though another planned file changed.");
        AssertNoArtifacts(directory);
    }

    private void PhaseG(PhaseContext phase)
    {
        string directory = phase.Directory;
        const string text = "A verified backup is required before installation.";
        string path = Path.Combine(directory, "backup-failure.txt");
        Write(directory, "backup-failure.txt", text, Encoding.Unicode, writeBom: true);
        Directory.CreateDirectory(path + ".bak");

        Dictionary<string, string> before = phase.CaptureBefore();

        using var gui = new EcGuiDriver(_app);
        System.Windows.Automation.AutomationElement review = gui.OpenReview(directory, 1);
        gui.Proceed(review);

        Check(Hash(path) == before["backup-failure.txt"],
            "The source changed even though its backup could not be created.");
        Check(Directory.Exists(path + ".bak"),
            "The conflicting backup directory was unexpectedly removed.");
        Check(!File.Exists(path + ".ecmeta.json"),
            "Recovery metadata was written for a conversion that did not install.");
    }

    private static Encoding CodePage(string name) =>
        Encoding.GetEncoding(
            name,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

    private static void Write(
        string directory,
        string name,
        string text,
        Encoding encoding,
        bool writeBom = false)
    {
        string path = Path.Combine(directory, name);
        byte[] content = encoding.GetBytes(text);

        if (!writeBom)
        {
            File.WriteAllBytes(path, content);
            return;
        }

        byte[] preamble = encoding.GetPreamble();
        byte[] bytes = new byte[preamble.Length + content.Length];
        preamble.CopyTo(bytes, 0);
        content.CopyTo(bytes, preamble.Length);
        File.WriteAllBytes(path, bytes);
    }

    private static void AssertUtf8(string path, string expected)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Check(!bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()),
            $"'{Path.GetFileName(path)}' unexpectedly contains a UTF-8 BOM.");
        Check(StrictUtf8.GetString(bytes) == expected,
            $"'{Path.GetFileName(path)}' did not preserve the expected Unicode text.");
    }

    private static void AssertRecovery(
        string sourcePath,
        string sourceHash,
        string sourceMode,
        int sourceCodePage)
    {
        string backup = sourcePath + ".bak";
        string metadata = sourcePath + ".ecmeta.json";

        Check(File.Exists(backup), $"Backup missing for '{Path.GetFileName(sourcePath)}'.");
        Check(File.Exists(metadata),
            $"Recovery metadata missing for '{Path.GetFileName(sourcePath)}'.");
        Check(Hash(backup) == sourceHash,
            $"Backup hash differs from the approved source for '{Path.GetFileName(sourcePath)}'.");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(metadata));
        JsonElement record = document.RootElement;

        Check(record.GetProperty("InstallationState").GetString() == "Completed",
            "Recovery metadata does not record a completed installation.");
        Check(record.GetProperty("OriginalSha256").GetString() == sourceHash,
            "Recovery metadata contains the wrong original hash.");
        Check(record.GetProperty("BackupSha256").GetString() == sourceHash,
            "Recovery metadata contains the wrong backup hash.");
        Check(record.GetProperty("ExpectedOutputSha256").GetString() == Hash(sourcePath),
            "Recovery metadata contains the wrong output hash.");
        Check(record.GetProperty("SourceEncodingMode").GetString() == sourceMode,
            "Recovery metadata contains the wrong source-selection mode.");
        Check(record.GetProperty("SourceEncodingId").GetInt32() == sourceCodePage,
            "Recovery metadata contains the wrong source codec identity.");
        Check(record.GetProperty("TargetEncodingId").GetInt32() == 65001,
            "Recovery metadata contains the wrong target codec identity.");
        Check(!record.GetProperty("TargetHasBom").GetBoolean(),
            "Recovery metadata contains the wrong target BOM policy.");
        Check(record.GetProperty("SourceTextSha256").GetString() ==
              record.GetProperty("OutputTextSha256").GetString(),
            "Recovery metadata does not prove equal source and output text hashes.");
    }

    private static void AssertSameFiles(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        Check(expected.Count == actual.Count,
            $"The file count changed ({expected.Count} before, {actual.Count} after).");

        foreach ((string name, string hash) in expected)
        {
            Check(actual.TryGetValue(name, out string? current) && current == hash,
                $"'{name}' was added, removed, or changed.");
        }
    }

    private static void AssertNoArtifacts(string directory)
    {
        string[] artifacts = Directory.GetFileSystemEntries(directory)
            .Where(path =>
                path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".ecmeta.json", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".unicodechecker.tmp", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Check(artifacts.Length == 0,
            $"Unexpected recovery artifacts: {string.Join(", ", artifacts)}");
    }

    private static Dictionary<string, string> Snapshot(string directory) =>
        Directory.EnumerateFiles(directory)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                path => Path.GetFileName(path)!,
                Hash,
                StringComparer.OrdinalIgnoreCase);

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class PhaseContext(string directory)
    {
        internal string Directory { get; } = directory;
        internal Dictionary<string, string> Before { get; private set; } = [];

        internal Dictionary<string, string> CaptureBefore()
        {
            Before = Snapshot(Directory);
            return Before;
        }
    }
}
