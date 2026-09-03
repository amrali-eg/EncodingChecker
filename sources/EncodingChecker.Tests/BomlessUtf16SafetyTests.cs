using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// Covers the one Unicode case where a successful heuristic is still not enough to
/// authorize an automatic rewrite: UTF-16 without a byte-order mark.
/// </summary>
public sealed class BomlessUtf16SafetyTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_bomless_utf16_").FullName;

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

    private ConversionReportEntry Convert(
        string fileName,
        byte[] bytes,
        string? sourceEncoding = null,
        bool whatIf = false,
        bool backup = true)
    {
        string path = Path.Combine(_root, fileName);
        File.WriteAllBytes(path, bytes);

        var completed = new EntrySink();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                IncludeSubdirectories = true,
                IncludePatterns = [fileName],
                Action = ScanAction.Convert,
                SourceCharset = sourceEncoding,
                TargetCharset = "utf-8",
                TargetWriteBom = false,
                WhatIf = whatIf,
                Backup = backup,
            },
            completed.Add,
            CancellationToken.None);

        return Assert.Single(completed);
    }

    [Fact]
    public void AutomaticBomlessUtf16BeMisreadAsLittleEndian_IsRefusedBeforePreviewBackupOrWrite()
    {
        // UTF-16BE reads these bytes as U+4100, U+0A00, U+4200. The same bytes read as
        // UTF-16LE look like A, LF, B, so the shared detector reasonably prefers LE.
        // Its preference still cannot establish what the original author intended.
        string authoritativeText = string.Concat(
            Enumerable.Repeat("\u4100\u0A00\u4200", 20));
        var utf16Be = new UnicodeEncoding(
            bigEndian: true,
            byteOrderMark: false,
            throwOnInvalidBytes: true);
        byte[] original = utf16Be.GetBytes(authoritativeText);

        ConversionReportEntry entry = Convert(
            "ambiguous-utf16be.txt", original, whatIf: false, backup: true);

        Assert.Equal(ConversionRowResult.Refused, entry.Result);
        Assert.Equal(PlannedAction.Refuse, entry.Action);
        Assert.Equal(ConversionReasonCodes.AmbiguousBomlessUtf16, entry.ReasonCode);
        Assert.Contains("these bytes are also valid UTF-16BE", entry.Diagnostic);
        Assert.Equal(original, File.ReadAllBytes(entry.FilePath));
        Assert.Equal(authoritativeText, utf16Be.GetString(File.ReadAllBytes(entry.FilePath)));
        Assert.False(File.Exists(entry.FilePath + ".bak"));
        Assert.False(File.Exists(ConversionMetadataStore.MetadataPathFor(entry.FilePath)));
    }

    [Fact]
    public void AmbiguousBomlessUtf16Preview_IsRefusedInsteadOfReportedAsConvertible()
    {
        byte[] original = new UnicodeEncoding(
            bigEndian: true,
            byteOrderMark: false,
            throwOnInvalidBytes: true)
            .GetBytes(string.Concat(Enumerable.Repeat("\u4100\u0A00\u4200", 20)));

        ConversionReportEntry entry = Convert(
            "preview-ambiguous-utf16be.txt", original, whatIf: true, backup: true);

        // A preview must describe the same safe refusal a real run would make. Before
        // this rule, the preview incorrectly promised conversion based on the LE guess.
        Assert.Equal(ConversionRowResult.Refused, entry.Result);
        Assert.Equal(ConversionReasonCodes.AmbiguousBomlessUtf16, entry.ReasonCode);
        Assert.Equal(original, File.ReadAllBytes(entry.FilePath));
        Assert.False(File.Exists(entry.FilePath + ".bak"));
        Assert.False(File.Exists(ConversionMetadataStore.MetadataPathFor(entry.FilePath)));
    }

    [Fact]
    public void AutomaticBomlessUtf16WhoseOppositeOrderIsInvalid_StillConverts()
    {
        // U+00D8 becomes D8 00 in UTF-16LE. Interpreted as BE, D8 00 is an unpaired
        // surrogate, so the bytes themselves prove that LE is the only valid order.
        string text = string.Concat(Enumerable.Repeat("Øline\n", 20));
        byte[] original = Encoding.Unicode.GetBytes(text);

        ConversionReportEntry entry = Convert("provable-utf16le.txt", original, backup: false);

        Assert.Equal(ConversionRowResult.Converted, entry.Result);
        Assert.Equal(PlannedAction.Convert, entry.Action);
        Assert.Equal(text, Encoding.UTF8.GetString(File.ReadAllBytes(entry.FilePath)));
    }

    [Fact]
    public void ExplicitSourceMatchingTheEstimate_IsStillReportedAsUnprovable()
    {
        // The case that shipped silent. Detection prefers little-endian for these
        // bytes and is wrong; a caller who repeats that guess destroys the file. EC
        // had already refused it as unprovable, so it holds the one fact that would
        // warn them - and reported nothing, because the choice agreed with the guess.
        // Agreeing with an estimate EC cannot prove is not corroboration.
        string authoritativeText = string.Concat(
            Enumerable.Repeat("䄀਀䈀", 20));
        var utf16Be = new UnicodeEncoding(
            bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);

        ConversionReportEntry entry = Convert(
            "matches-estimate.txt",
            utf16Be.GetBytes(authoritativeText),
            sourceEncoding: "utf-16");

        Assert.Equal(ConversionRowResult.Converted, entry.Result);
        Assert.Equal(
            ConversionReasonCodes.ExplicitSourceOnUnprovableBomlessUnicode,
            entry.ReasonCode);
        Assert.Contains("could not be established", entry.Diagnostic);
        Assert.Contains("taken on trust", entry.Diagnostic);
    }

    [Fact]
    public void ExplicitSourceContradictingTheEstimate_KeepsItsOwnReasonCode()
    {
        // The two situations must stay distinguishable in the machine-readable field:
        // one caller contradicted a guess, the other repeated it, and a script reading
        // reports should be able to tell which.
        string authoritativeText = string.Concat(
            Enumerable.Repeat("䄀਀䈀", 20));
        var utf16Be = new UnicodeEncoding(
            bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);

        ConversionReportEntry entry = Convert(
            "differs-from-estimate.txt",
            utf16Be.GetBytes(authoritativeText),
            sourceEncoding: "utf-16BE");

        Assert.Equal(ConversionRowResult.Converted, entry.Result);
        Assert.Equal(
            ConversionReasonCodes.ExplicitSourceDiffersFromBomlessUnicodeEstimate,
            entry.ReasonCode);
    }

    [Fact]
    public void ExplicitSourceOnAProvableFile_IsNotReported()
    {
        // The advisory must not fire for every explicit UTF-16 choice, only where the
        // byte order could not be established. U+00D8 byte-swaps to an unpaired
        // surrogate, so big-endian is structurally impossible and the order is proven.
        var utf16Le = new UnicodeEncoding(
            bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);

        ConversionReportEntry entry = Convert(
            "provable.txt",
            utf16Le.GetBytes("Øhello world, this is provable little-endian"),
            sourceEncoding: "utf-16");

        Assert.Equal(ConversionRowResult.Converted, entry.Result);
        Assert.Null(entry.ReasonCode);
    }

    [Fact]
    public void ExplicitUtf16SourceResolvesAnAmbiguousFileButKeepsEveryOtherSafetyCheck()
    {
        string authoritativeText = string.Concat(
            Enumerable.Repeat("\u4100\u0A00\u4200", 20));
        var utf16Be = new UnicodeEncoding(
            bigEndian: true,
            byteOrderMark: false,
            throwOnInvalidBytes: true);
        byte[] original = utf16Be.GetBytes(authoritativeText);

        ConversionReportEntry entry = Convert(
            "explicit-utf16be.txt", original, sourceEncoding: "utf-16BE", backup: true);

        Assert.Equal(ConversionRowResult.Converted, entry.Result);
        Assert.True(entry.SourceEncodingWasSpecified);
        Assert.Equal(authoritativeText, Encoding.UTF8.GetString(File.ReadAllBytes(entry.FilePath)));
        Assert.Equal(original, File.ReadAllBytes(entry.FilePath + ".bak"));
        Assert.True(File.Exists(ConversionMetadataStore.MetadataPathFor(entry.FilePath)));
    }
}
