using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// CheckUtf32's structural prefilter runs before CheckUtf16, so BOM-less UTF-16 reaches it
/// first. Testing only the most significant byte left an ASCII-range UTF-16 buffer looking
/// like a live UTF-32 candidate for its whole length, paying a full strict UTF-32 decode
/// before IsValidText rejected it. Bounding the next byte by 0x10 - the ceiling implied by
/// U+10FFFF - rejects it on the first code unit instead.
///
/// This is a prefilter optimisation, not a correctness fix: invalid UTF-32 was already
/// rejected downstream, and still is. Surrogates in particular are unaffected by the added
/// test and continue to rely on IsValidText.
/// </summary>
public sealed class Utf32PrefilterTests
{
    private static byte[] Encode(bool bigEndian, string text) =>
        new UTF32Encoding(bigEndian, byteOrderMark: false).GetBytes(text);

    private static byte[] RawLittleEndian(params uint[] scalars)
    {
        var bytes = new byte[scalars.Length * 4];

        for (int i = 0; i < scalars.Length; i++)
            BitConverter.GetBytes(scalars[i]).CopyTo(bytes, i * 4);

        return bytes;
    }

    private static string Repeat(string s, int times) =>
        string.Concat(Enumerable.Repeat(s, times));

    // ---- valid UTF-32 must still be detected ----

    [Theory]
    [InlineData(false, "utf-32")]
    [InlineData(true, "utf-32BE")]
    public void AsciiText_IsStillDetected(bool bigEndian, string expected)
    {
        byte[] bytes = Encode(bigEndian, Repeat("Hello, World! ", 8));

        Assert.Equal(expected, TextEncoding.DetectFromBuffer(bytes)?.WebName);
    }

    [Theory]
    [InlineData(false, "utf-32")]
    [InlineData(true, "utf-32BE")]
    public void CjkText_IsStillDetected(bool bigEndian, string expected)
    {
        byte[] bytes = Encode(bigEndian, Repeat("東京都渋谷区 ", 8));

        Assert.Equal(expected, TextEncoding.DetectFromBuffer(bytes)?.WebName);
    }

    [Theory]
    [InlineData(false, "utf-32")]
    [InlineData(true, "utf-32BE")]
    public void SupplementaryPlaneText_IsStillDetected(bool bigEndian, string expected)
    {
        // Emoji sit above U+FFFF, so the byte the new test inspects is non-zero here -
        // the case most at risk of being rejected by an over-eager bound.
        byte[] bytes = Encode(bigEndian, Repeat("🌍🎉𠜎 ", 8));

        Assert.Equal(expected, TextEncoding.DetectFromBuffer(bytes)?.WebName);
    }

    [Fact]
    public void TheExactUpperBoundScalar_IsStillAccepted()
    {
        // U+10FFFF puts 0x10 in the tested byte: the boundary the comparison must not
        // exclude. Padded with ASCII so LooksLikeText sees ordinary text either side.
        uint[] scalars =
        [
            .. Enumerable.Repeat((uint)'a', 20),
            0x10FFFF,
            .. Enumerable.Repeat((uint)'b', 20),
        ];

        Assert.Equal("utf-32", TextEncoding.DetectFromBuffer(RawLittleEndian(scalars))?.WebName);
    }

    [Fact]
    public void OneAboveTheUpperBound_IsRejected()
    {
        uint[] scalars =
        [
            .. Enumerable.Repeat((uint)'a', 20),
            0x110000,
            .. Enumerable.Repeat((uint)'b', 20),
        ];

        Assert.NotEqual("utf-32", TextEncoding.DetectFromBuffer(RawLittleEndian(scalars))?.WebName);
    }

    // ---- UTF-16 must not be mistaken for a UTF-32 candidate ----

    [Theory]
    [InlineData(false, "utf-16")]
    [InlineData(true, "utf-16BE")]
    public void Utf16Text_IsDetectedAsUtf16NotUtf32(bool bigEndian, string expected)
    {
        // The motivating case: ASCII-range UTF-16 has a zero in every other byte, which
        // the most-significant-byte test alone could not rule out.
        byte[] bytes = new UnicodeEncoding(bigEndian, byteOrderMark: false)
            .GetBytes(Repeat("Hello, World! ", 40));

        Assert.Equal(expected, TextEncoding.DetectFromBuffer(bytes)?.WebName);
    }

    // ---- invalid scalars stay rejected by the authoritative stage ----

    [Theory]
    [InlineData(0xD800u)]
    [InlineData(0xDFFFu)]
    public void SurrogateScalars_AreStillRejected(uint surrogate)
    {
        // Not rejected by the prefilter - the tested byte is zero for these - so this
        // pins that IsValidText still catches them.
        uint[] scalars =
        [
            .. Enumerable.Repeat((uint)'a', 20),
            surrogate,
            .. Enumerable.Repeat((uint)'b', 20),
        ];

        Assert.NotEqual("utf-32", TextEncoding.DetectFromBuffer(RawLittleEndian(scalars))?.WebName);
    }

    [Fact]
    public void AllSurrogates_AreRejected()
    {
        byte[] bytes = RawLittleEndian([.. Enumerable.Repeat(0xD800u, 40)]);

        Assert.Null(TextEncoding.DetectFromBuffer(bytes));
    }

    [Fact]
    public void BomPrefixedUtf32_IsUnaffectedByThePrefilter()
    {
        // BOM detection happens before the structural scan and must keep winning.
        byte[] bytes = new UTF32Encoding(bigEndian: false, byteOrderMark: true)
            .GetPreamble()
            .Concat(Encode(false, Repeat("Hello ", 8)))
            .ToArray();

        Assert.Equal("utf-32", TextEncoding.DetectFromBuffer(bytes)?.WebName);
    }
}
