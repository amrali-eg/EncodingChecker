using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// The contract for <c>-From</c>, and the escape from an ambiguity refusal.
///
/// The refusal message tells a user to specify the source encoding, so specifying it has
/// to work — otherwise the safety feature issues advice its own interface cannot take.
///
/// But it replaces detection and nothing else. Every guarantee from the conversion engine
/// still holds: the bytes must strictly decode as the named encoding, the output must
/// re-decode to exactly the same text, and a failed backup still aborts. <c>-From</c>
/// answers "which encoding is this?", not "convert it regardless".
/// </summary>
public sealed class ExplicitSourceEncodingTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_from_").FullName;

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

    private string Write(string name, byte[] content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private List<ConversionReportEntry> Scan(string? from, bool backup = false)
    {
        var results = new List<ConversionReportEntry>();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                IncludeSubdirectories = true,
                IncludePatterns = ["*"],
                Action = ScanAction.Convert,
                TargetCharset = "utf-8",
                TargetWriteBom = false,
                SourceCharset = from,
                Backup = backup,
            },
            results.Add,
            CancellationToken.None);

        return results;
    }

    [Fact]
    public void AmbiguousAutoDetection_IsRefused()
    {
        Write("ambiguous.txt",
            Encoding.GetEncoding("windows-1252").GetBytes("Le café était déjà prêt"));

        ConversionReportEntry entry = Assert.Single(Scan(from: null));

        Assert.Equal(ConversionRowResult.Error, entry.Result);
        Assert.Contains("could not be determined uniquely", entry.Diagnostic);
    }

    [Fact]
    public void SameTextAlternativeCodecs_AreAllowed()
    {
        // Every candidate agrees on the content, so the label is undetermined and the
        // text is not. There is nothing to protect the user from.
        Write("ascii.txt", "plain ascii, no high bytes"u8.ToArray());

        ConversionReportEntry entry = Assert.Single(Scan(from: null));

        Assert.NotEqual(ConversionRowResult.Error, entry.Result);
    }

    [Fact]
    public void ExplicitSource_LetsTheConversionProceed()
    {
        // The same file the detector refuses. Somebody has now said which encoding it is.
        const string text = "Le café était déjà prêt";
        string path = Write("resolved.txt",
            Encoding.GetEncoding("windows-1252").GetBytes(text));

        ConversionReportEntry entry = Assert.Single(Scan(from: "windows-1252"));

        Assert.Equal(ConversionRowResult.Converted, entry.Result);
        Assert.Equal(text, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        Assert.True(entry.SourceEncodingWasSpecified);
    }

    [Fact]
    public void ExplicitSource_ChangesTheAnswerNotJustThePermission()
    {
        // Naming a different encoding for the same bytes must produce different text.
        // If it did not, -From would be decoration rather than a decision.
        byte[] bytes = Encoding.GetEncoding("windows-1252").GetBytes("café");
        string path = Write("interpretation.txt", bytes);

        Assert.Equal(ConversionRowResult.Converted,
            Assert.Single(Scan(from: "koi8-r")).Result);

        string asKoi8 = Encoding.UTF8.GetString(File.ReadAllBytes(path));

        Assert.NotEqual("café", asKoi8);
        Assert.Equal(Encoding.GetEncoding("koi8-r").GetString(bytes), asKoi8);
    }

    [Fact]
    public void ExplicitSource_WithBytesItCannotDecode_IsStillRefused()
    {
        // EUC-JP bytes carrying a JIS X 0212 sequence code page 51932 cannot map.
        // Naming the encoding does not make the bytes representable.
        byte[] unrepresentable =
            [0x8F, 0xB0, 0xDF, 0xB9, 0xA5, 0xA1, 0xA4, 0xC0, 0xA4, 0xB3];
        string path = Write("undecodable.txt", unrepresentable);

        ConversionReportEntry entry = Assert.Single(Scan(from: "euc-jp"));

        Assert.Equal(ConversionRowResult.Error, entry.Result);
        Assert.Equal(unrepresentable, File.ReadAllBytes(path));
    }

    [Fact]
    public void ExplicitSource_WithContentTheTargetCannotHold_IsStillRefused()
    {
        // Converting to a target that cannot represent the text must fail whether the
        // source encoding was detected or chosen.
        string path = Path.Combine(_root, "unencodable.txt");
        byte[] original = Encoding.UTF8.GetBytes("世界 مرحبا");
        File.WriteAllBytes(path, original);

        var results = new List<ConversionReportEntry>();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                IncludeSubdirectories = true,
                IncludePatterns = ["*"],
                Action = ScanAction.Convert,
                TargetCharset = "windows-1252",
                TargetWriteBom = false,
                SourceCharset = "utf-8",
            },
            results.Add,
            CancellationToken.None);

        ConversionReportEntry entry = Assert.Single(results);

        Assert.Equal(ConversionRowResult.Error, entry.Result);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void ExplicitSource_WithAFailingBackup_IsStillRefused()
    {
        byte[] original = Encoding.GetEncoding("windows-1252").GetBytes("café");
        string path = Write("backupfail.txt", original);
        Directory.CreateDirectory(path + ".bak");

        ConversionReportEntry entry =
            Assert.Single(Scan(from: "windows-1252", backup: true));

        Assert.Equal(ConversionRowResult.Error, entry.Result);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void ExplicitSource_StillWritesTheRecoveryRecord()
    {
        // Choosing the encoding does not opt out of being able to undo the result.
        string path = Write("recorded.txt",
            Encoding.GetEncoding("windows-1252").GetBytes("café"));

        Assert.Equal(ConversionRowResult.Converted,
            Assert.Single(Scan(from: "windows-1252", backup: true)).Result);

        RestoreStatus status = ConversionMetadataStore.Inspect(path);

        Assert.Equal(RestoreAvailability.Available, status.Availability);
        Assert.Equal(1252, status.Metadata!.DetectedCodePage);
    }

    [Fact]
    public void DetectionModeIsRecordedSoTheTwoClaimsStayDistinct()
    {
        // Detection can be wrong in ways an explicit choice cannot. A later journal
        // needs to know which one produced a conversion.
        Write("ascii.txt", "plain ascii"u8.ToArray());

        Assert.False(Assert.Single(Scan(from: null)).SourceEncodingWasSpecified);
        Assert.True(Assert.Single(Scan(from: "windows-1252")).SourceEncodingWasSpecified);
    }
}
