using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// BOM-less UTF-32 is an estimate detection cannot establish, so it is refused.
/// </summary>
/// <remarks>
/// Two silent-corruption paths closed by one rule. A BOM-less UTF-16 file with one
/// character per line puts a C0 control in every second code unit, so each four-byte group
/// is an in-range unassigned scalar and the file decodes as UTF-32 — converting it rewrites
/// different text. Separately, genuine BOM-less UTF-32 can be valid under both byte orders,
/// and detection preferred little-endian without saying so.
///
/// An opposite-order test cannot close the first: the UTF-16 file's bytes are *not* valid
/// under the opposite UTF-32 order, so the ambiguity check that protects UTF-16 returns
/// false. What cannot be proven is the codec, not just its byte order.
///
/// The cost is that BOM-less UTF-32 no longer converts automatically even when its content
/// is unambiguous. That is deliberate: nothing in the bytes distinguishes it from the
/// UTF-16 file above.
/// </remarks>
public sealed class BomlessUtf32RefusalTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_utf32_").FullName;

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

    private string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private ConversionReportEntry Convert(string? from = null)
    {
        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            TargetCharset = "utf-8",
            TargetWriteBom = false,
            SourceCharset = from,
        };

        var entries = new EntrySink();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);
        return Assert.Single(entries);
    }

    /// <summary>Finding 1: UTF-16 that also decodes as UTF-32 must not be converted.</summary>
    [Fact]
    public void Utf16ThatAlsoDecodesAsUtf32IsRefusedRatherThanRewritten()
    {
        // One ASCII character per LF-terminated line, UTF-16LE, no BOM.
        string text = string.Concat(
            Enumerable.Range(0, 200).Select(i => $"{(char)('a' + (i % 26))}\n"));

        string path = Write("perline.txt", Encoding.Unicode.GetBytes(text));
        byte[] before = File.ReadAllBytes(path);

        ConversionReportEntry entry = Convert();

        Assert.Equal(PlannedAction.Refuse, entry.Action);
        Assert.Equal(ConversionReasonCodes.UnprovableBomlessUtf32, entry.ReasonCode);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    /// <summary>Finding 2: UTF-32 valid under both byte orders must not be converted.</summary>
    [Fact]
    public void Utf32ValidUnderBothByteOrdersIsRefusedRatherThanGuessed()
    {
        // Every scalar a multiple of 0x100, so the bytes read equally well either way.
        byte[] bytes = [.. Enumerable.Range(1, 60)
            .SelectMany(i => new byte[] { 0x00, (byte)i, 0x00, 0x00 })];

        string path = Write("bothways.txt", bytes);
        byte[] before = File.ReadAllBytes(path);

        ConversionReportEntry entry = Convert();

        Assert.Equal(PlannedAction.Refuse, entry.Action);
        Assert.Equal(ConversionReasonCodes.UnprovableBomlessUtf32, entry.ReasonCode);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    /// <summary>
    /// The cost, stated as a test: ordinary BOM-less UTF-32 is refused too.
    /// </summary>
    [Fact]
    public void OrdinaryBomlessUtf32IsRefusedAsWell()
    {
        string path = Write(
            "plain.txt", new UTF32Encoding(false, false).GetBytes("Hello world\n"));

        ConversionReportEntry entry = Convert();

        Assert.Equal(PlannedAction.Refuse, entry.Action);
        Assert.Equal(ConversionReasonCodes.UnprovableBomlessUtf32, entry.ReasonCode);
    }

    /// <summary>Naming the source is the documented way through, so it must work.</summary>
    [Fact]
    public void AnExplicitSourceStillConvertsBomlessUtf32()
    {
        const string text = "Hello world\n";
        Write("plain.txt", new UTF32Encoding(false, false).GetBytes(text));

        ConversionReportEntry entry = Convert(from: "utf-32");

        Assert.Equal(PlannedAction.Convert, entry.Action);
        Assert.Equal(
            text,
            File.ReadAllText(Path.Combine(_root, "plain.txt"), new UTF8Encoding(false)));
    }

    /// <summary>A BOM settles the codec, so UTF-32 with one still converts.</summary>
    [Fact]
    public void Utf32WithABomStillConverts()
    {
        const string text = "Hello world\n";
        var utf32 = new UTF32Encoding(false, true);
        Write("bom.txt", [.. utf32.GetPreamble(), .. utf32.GetBytes(text)]);

        ConversionReportEntry entry = Convert();

        Assert.Equal(PlannedAction.Convert, entry.Action);
    }

    /// <summary>
    /// Private-use characters must keep converting: icon fonts put them in ordinary text
    /// files, so a rule that rejected unassigned or private-use scalars would break real
    /// sources. This fix deliberately changes no scalar classification.
    /// </summary>
    [Fact]
    public void PrivateUseCharactersStillConvert()
    {
        // U+E000 and U+F8FF bracket the BMP private use area; U+F0000 is plane 15.
        string text = "icon   " + char.ConvertFromUtf32(0xF0000) + "\n";
        var utf16 = new UnicodeEncoding(false, true);
        Write("icons.txt", [.. utf16.GetPreamble(), .. utf16.GetBytes(text)]);

        ConversionReportEntry entry = Convert();

        Assert.Equal(PlannedAction.Convert, entry.Action);
        Assert.Equal(
            text,
            File.ReadAllText(Path.Combine(_root, "icons.txt"), new UTF8Encoding(false)));
    }
}
