using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// A corpus audit of 5,078 files found 262 where the bytes do not identify the encoding
/// that wrote them: single-byte code pages map 256 values independently, so a file valid
/// in windows-1252 is equally valid in iso-8859-1 and no inspection decides between them.
/// Detection still answers, and converting on that answer rewrites the file into one of
/// several possible readings without saying so.
///
/// These pin the distinction the refusal rests on. Two failure directions matter equally:
/// converting a genuinely ambiguous file, and refusing one whose encoding the bytes do
/// determine. A gate that refuses everything is not safe, it is broken.
/// </summary>
public sealed class EncodingAmbiguityTests
{
    private static AmbiguityAnalysis Analyze(string text, string charset) =>
        EncodingAmbiguity.Analyze(
            Encoding.GetEncoding(charset).GetBytes(text),
            Encoding.GetEncoding(charset));

    [Theory]
    [InlineData("utf-8", "Hello 世界 café")]
    [InlineData("shift_jis", "こんにちは世界。日本語のテキスト")]
    [InlineData("euc-jp", "こんにちは世界")]
    [InlineData("big5", "你好世界。這是繁體中文")]
    [InlineData("gb18030", "这是简体中文文本")]
    [InlineData("euc-kr", "안녕하세요 세계")]
    [InlineData("utf-16", "Hello 世界 café")]
    public void StructuredEncodingsAreNotTreatedAsAmbiguous(string charset, string text)
    {
        // These constrain their byte sequences, so a file valid under one was not valid
        // by accident. Other codecs "reading" it differently are codecs that cannot
        // refuse anything, which is not a competing claim.
        AmbiguityAnalysis analysis = Analyze(text, charset);

        Assert.NotEqual(AmbiguityClass.TextChanging, analysis.Class);
        Assert.True(analysis.IsSafeToConvertAutomatically);
    }

    [Theory]
    [InlineData("windows-1252", "Le café était déjà prêt")]
    [InlineData("koi8-r", "Привет мир")]
    [InlineData("iso-8859-7", "Γειά σου κόσμε")]
    public void SingleByteTextWithNoDistinguishingStructureIsAmbiguous(
        string charset, string text)
    {
        AmbiguityAnalysis analysis = Analyze(text, charset);

        Assert.Equal(AmbiguityClass.TextChanging, analysis.Class);
        Assert.False(analysis.IsSafeToConvertAutomatically);
        Assert.NotEmpty(analysis.CompetingCandidates);
    }

    [Fact]
    public void PureAsciiIsAmbiguousInLabelButNotInText()
    {
        // Every candidate agrees on what this file says, so the label is undetermined and
        // the content is not. Refusing here would protect nothing.
        AmbiguityAnalysis analysis = EncodingAmbiguity.Analyze(
            "plain ascii, no high bytes at all"u8, Encoding.ASCII);

        Assert.NotEqual(AmbiguityClass.TextChanging, analysis.Class);
        Assert.True(analysis.IsSafeToConvertAutomatically);
        Assert.Empty(analysis.CompetingCandidates);
    }

    [Fact]
    public void TheRefusalNamesTheEncodingsActuallyInConflict()
    {
        // "Low confidence" gives a user nothing to act on. The competing encodings and
        // the next step do.
        AmbiguityAnalysis analysis = Analyze("Le café était déjà prêt", "windows-1252");

        string message = analysis.Describe("windows-1252");

        Assert.Contains("could not be determined uniquely", message);
        Assert.Contains("windows-1252", message);
        Assert.Contains("would produce different text", message);
        Assert.Contains("specify the source encoding explicitly", message);
    }

    [Fact]
    public void TheSameBytesAlwaysGetTheSameAnswer()
    {
        // An earlier version sampled probe positions randomly and moved with the seed on
        // short files. Whether a conversion is refused must not depend on a random draw.
        byte[] bytes = Encoding.GetEncoding("windows-1252").GetBytes("Le café était déjà prêt");
        Encoding detected = Encoding.GetEncoding("windows-1252");

        AmbiguityClass first = EncodingAmbiguity.Analyze(bytes, detected).Class;

        for (int i = 0; i < 8; i++)
            Assert.Equal(first, EncodingAmbiguity.Analyze(bytes, detected).Class);
    }

    [Fact]
    public void AliasesOfOneEncodingAreNotCountedAsRivalReadings()
    {
        // The candidate set is deduplicated by code page. cp949 and ks_c_5601-1987 name
        // one encoding, and listing both would manufacture a disagreement out of a
        // spelling difference - a mistake this project has made before, in the audit that
        // compared detector output by label.
        AmbiguityAnalysis analysis = Analyze("안녕하세요 세계", "euc-kr");

        Assert.Equal(
            analysis.CompetingCandidates.Count,
            analysis.CompetingCandidates.Distinct().Count());
    }

    [Fact]
    public void AnEmptySampleIsNotClaimedToBeAmbiguous()
    {
        AmbiguityAnalysis analysis = EncodingAmbiguity.Analyze(
            ReadOnlySpan<byte>.Empty, Encoding.UTF8);

        Assert.True(analysis.IsSafeToConvertAutomatically);
    }
}
