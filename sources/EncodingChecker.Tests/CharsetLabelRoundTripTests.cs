namespace EncodingChecker.Tests;

/// <summary>ParseCharsetLabel/FormatCharsetLabel round-trip for every BOM-aware charset.</summary>
public sealed class CharsetLabelRoundTripTests
{
    public static IEnumerable<object[]> BomAwareCharsets()
    {
        yield return ["utf-8"];
        yield return ["utf-16"];
        yield return ["utf-16BE"];
        yield return ["utf-32"];
        yield return ["utf-32BE"];
    }

    [Theory]
    [MemberData(nameof(BomAwareCharsets))]
    public void ParseCharsetLabel_BomSuffixedLabel_ParsesBaseCharsetAndBomFlag(string baseCharset)
    {
        string bomLabel = baseCharset + "-bom";

        ScanEngine.ParseCharsetLabel(bomLabel, out string parsedBase, out bool hasBom);

        Assert.True(hasBom);
        Assert.Equal(baseCharset, parsedBase);
    }

    [Theory]
    [MemberData(nameof(BomAwareCharsets))]
    public void ParseCharsetLabel_BareLabel_ParsesAsNoBom(string baseCharset)
    {
        ScanEngine.ParseCharsetLabel(baseCharset, out string parsedBase, out bool hasBom);

        Assert.False(hasBom);
        Assert.Equal(baseCharset, parsedBase);
    }

    [Theory]
    [MemberData(nameof(BomAwareCharsets))]
    public void FormatCharsetLabel_RoundTripsBackToTheOriginalLabels(string baseCharset)
    {
        Assert.Equal(baseCharset, ScanEngine.FormatCharsetLabel(baseCharset, hasBom: false));
        Assert.Equal(baseCharset + "-bom", ScanEngine.FormatCharsetLabel(baseCharset, hasBom: true));
    }

    [Theory]
    [MemberData(nameof(BomAwareCharsets))]
    public void ParseThenFormat_IsTheIdentityForBothBomStates(string baseCharset)
    {
        string bomLabel = baseCharset + "-bom";

        ScanEngine.ParseCharsetLabel(bomLabel, out string parsedBomBase, out bool bomFlag);
        Assert.Equal(bomLabel, ScanEngine.FormatCharsetLabel(parsedBomBase, bomFlag));

        ScanEngine.ParseCharsetLabel(baseCharset, out string parsedBareBase, out bool bareFlag);
        Assert.Equal(baseCharset, ScanEngine.FormatCharsetLabel(parsedBareBase, bareFlag));
    }

    [Fact]
    public void ParseCharsetLabel_IsCaseInsensitiveForTheBomSuffix()
    {
        ScanEngine.ParseCharsetLabel("utf-8-BOM", out string baseCharset, out bool hasBom);

        Assert.True(hasBom);
        Assert.Equal("utf-8", baseCharset);
    }

    [Theory]
    [MemberData(nameof(BomAwareCharsets))]
    public void IsBomCapable_RecognizesEveryEncodingOfferedWithABom(string charset) =>
        Assert.True(ScanEngine.IsBomCapable(charset));

    [Fact]
    public void DescribeTarget_DoesNotInventABomForAscii()
    {
        Assert.False(ScanEngine.IsBomCapable("us-ascii"));
        Assert.Equal("us-ascii", ScanEngine.DescribeTarget("us-ascii", hasBom: false));
        Assert.Equal("utf-8 without a BOM", ScanEngine.DescribeTarget("utf-8", hasBom: false));
    }
}
