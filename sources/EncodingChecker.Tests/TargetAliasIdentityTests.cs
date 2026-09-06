using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace EncodingChecker.Tests;

/// <summary>
/// A file already in the target encoding must be left alone however the target was
/// spelled. Codec identity is the code page: "utf-16", "unicode", "ucs-2" and "utf-16le"
/// all name code page 1200, and comparing the labels instead rewrote every such file to
/// identical bytes, discarding its timestamp and creating a backup on the way.
/// </summary>
public sealed class TargetAliasIdentityTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec-alias-").FullName;

    private readonly string _path;

    public TargetAliasIdentityTests() => _path = Path.Combine(_root, "file.txt");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory cannot make a passing test wrong.
        }
    }

    private ConversionReportEntry Convert(string targetLabel)
    {
        ScanEngine.ParseCharsetLabel(targetLabel, out string charset, out bool writeBom);

        var entries = new EntrySink();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                Action = ScanAction.Convert,
                TargetCharset = charset,
                TargetWriteBom = writeBom,
                Backup = true,
                MaxParallelism = 1,
            },
            entries.Add,
            CancellationToken.None);

        return entries.Single();
    }

    private static byte[] Utf16LeWithBom(string text) =>
    [
        .. new UnicodeEncoding(bigEndian: false, byteOrderMark: true).GetPreamble(),
        .. new UnicodeEncoding(bigEndian: false, byteOrderMark: false).GetBytes(text),
    ];

    [Theory]
    [InlineData("utf-16-bom")]
    [InlineData("unicode-bom")]
    [InlineData("ucs-2-bom")]
    [InlineData("utf-16le-bom")]
    public void EveryAliasOfTheSourceCodecLeavesTheFileAlone(string targetLabel)
    {
        byte[] original = Utf16LeWithBom("hello alias world\n");
        File.WriteAllBytes(_path, original);
        DateTime written = File.GetLastWriteTimeUtc(_path);

        ConversionReportEntry entry = Convert(targetLabel);

        Assert.Equal(PlannedAction.Unchanged, entry.Action);
        Assert.Equal(ConversionRowResult.Unchanged, entry.Result);
        Assert.Equal(original, File.ReadAllBytes(_path));
        Assert.Equal(written, File.GetLastWriteTimeUtc(_path));

        // An unchanged file is never read or rewritten, so it earns no recovery artifacts.
        Assert.False(File.Exists(_path + ".bak"));
        Assert.Empty(Directory.GetFiles(_root, "*" + ConversionMetadataStore.Suffix));
    }

    [Theory]
    [InlineData("us-ascii")]
    [InlineData("ascii")]
    [InlineData("ANSI_X3.4-1968")]
    public void EveryAliasOfAsciiLeavesAnAsciiFileAlone(string targetLabel)
    {
        byte[] original = Encoding.ASCII.GetBytes("plain ascii content\n");
        File.WriteAllBytes(_path, original);
        DateTime written = File.GetLastWriteTimeUtc(_path);

        ConversionReportEntry entry = Convert(targetLabel);

        Assert.Equal(PlannedAction.Unchanged, entry.Action);
        Assert.Equal(original, File.ReadAllBytes(_path));
        Assert.Equal(written, File.GetLastWriteTimeUtc(_path));
    }

    [Fact]
    public void ABomlessUtf16FileIsUnchangedRatherThanRefusedUnderAnAliasTarget()
    {
        // Spelling the same codec differently used to route this file past the unchanged
        // test and into the BOM-less ambiguity refusal, so an alias moved the CLI from
        // exit 0 to exit 5 without any file differing.
        byte[] original = new UnicodeEncoding(bigEndian: false, byteOrderMark: false)
            .GetBytes("a list of ordinary words\n");

        File.WriteAllBytes(_path, original);

        ConversionReportEntry entry = Convert("utf-16le");

        Assert.Equal("utf-16", entry.SourceEncoding);
        Assert.False(entry.SourceHasBom);
        Assert.Equal(PlannedAction.Unchanged, entry.Action);
        Assert.Null(entry.ReasonCode);
        Assert.Equal(original, File.ReadAllBytes(_path));
    }

    [Fact]
    public void AsciiIsStillConvertedToUtf8BecauseTheyAreDifferentCodecs()
    {
        // Deliberately not folded into the unchanged test. Detection samples only the
        // first 64 KiB, so accepting an "us-ascii" label as already being UTF-8 would
        // pass silently over a file whose later bytes are neither.
        byte[] original = Encoding.ASCII.GetBytes("plain ascii content\n");
        File.WriteAllBytes(_path, original);

        ConversionReportEntry entry = Convert("utf-8");

        Assert.Equal(PlannedAction.Convert, entry.Action);
        Assert.Equal(ConversionRowResult.Converted, entry.Result);
    }

    [Fact]
    public void AnUnresolvedCodePageIsNotEvidenceOfSameness()
    {
        // 0 is the "did not resolve" sentinel plans and journals already use. Two of
        // them describe two unknowns, not one shared codec.
        Assert.NotEqual(
            PlannedAction.Unchanged,
            ConversionPolicy.Decide(
                "mystery", sourceCodePage: 0, sourceHasBom: false,
                "mystery", targetCodePage: 0, targetHasBom: false,
                sourceWasSpecified: false, isUnicodeOrAscii: false,
                explicitSourceConflictsWithReliableDetection: false,
                automaticBomlessUtf16IsAmbiguous: false,
                out _, out _));
    }

    [Fact]
    public void ADifferingBomPolicyStillConvertsTheSameCodec()
    {
        // The code page matches, so only the BOM decision separates these. It must still
        // be honoured, or "utf-8" and "utf-8-bom" would become the same request.
        byte[] original = Encoding.ASCII.GetBytes("plain ascii content\n");
        File.WriteAllBytes(_path, original);

        ConversionReportEntry entry = Convert("utf-8-bom");

        Assert.Equal(PlannedAction.Convert, entry.Action);
        Assert.Equal(
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble(),
            File.ReadAllBytes(_path).Take(3));
    }
}
