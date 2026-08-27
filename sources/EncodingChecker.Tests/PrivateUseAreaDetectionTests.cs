using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// LooksLikeText used to return false on the first private-use scalar, so one icon-font
/// glyph made an entire file undetectable - reported (Unknown) and skipped, never
/// converted. It also did so inconsistently: only the first 500 scalars are examined, so
/// the same character further into the file was accepted.
///
/// Private-use scalars are now excluded from the printable ratio like control characters,
/// which keeps the binary evidence (a buffer that is largely private-use still fails)
/// without rejecting ordinary text over a single character.
/// </summary>
public sealed class PrivateUseAreaDetectionTests
{
    // Private-use characters render as nothing and have been silently emptied by
    // file-editing tools in this repository before. TheFixtureConstantsAreActuallyPrivateUse
    // below fails loudly if that happens, rather than leaving these tests asserting
    // against ordinary text.
    private const string HomeIcon = "";     // Font Awesome "home"
    private const string ProfileIcon = "";
    private const string SaveIcon = "";
    private const string FirstPua = "";     // first private-use scalar

    private static string? Detect(string text, Encoding encoding) =>
        TextEncoding.DetectFromBuffer(encoding.GetBytes(text))?.WebName;

    private static string Repeat(string s, int times) =>
        string.Concat(Enumerable.Repeat(s, times));

    public static IEnumerable<object[]> Encodings() =>
    [
        [new UTF8Encoding(false), "utf-8"],
        [new UnicodeEncoding(false, false), "utf-16"],
    ];

    [Fact]
    public void TheFixtureConstantsAreActuallyPrivateUse()
    {
        // If any of these were emptied or flattened to ordinary characters, every test
        // below would still pass while testing nothing.
        foreach (string s in new[] { HomeIcon, ProfileIcon, SaveIcon, FirstPua })
        {
            Rune rune = Assert.Single(s.EnumerateRunes());

            Assert.Equal(
                System.Globalization.UnicodeCategory.PrivateUse,
                Rune.GetUnicodeCategory(rune));
        }
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void IconFontMarkup_IsDetected(Encoding encoding, string expected)
    {
        // The reported case: markup carrying icon glyphs was skipped entirely.
        string markup =
            $"""
             <nav class="menu">
               <i class="icon">{HomeIcon}</i> Home
               <i class="icon">{ProfileIcon}</i> Profile
               <i class="icon">{SaveIcon}</i> Save
             </nav>
             """;

        Assert.Equal(expected, Detect(markup, encoding));
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void OnePrivateUseScalarInOrdinaryText_IsDetected(Encoding encoding, string expected)
    {
        string text =
            $"A perfectly ordinary sentence with one {FirstPua} private-use character in it. " +
            "Followed by more ordinary prose so the sample is comfortably text-like.";

        Assert.Equal(expected, Detect(text, encoding));
    }

    [Fact]
    public void PositionOfThePrivateUseScalar_NoLongerChangesTheOutcome()
    {
        // The old behaviour depended on whether the scalar fell inside the 500-rune
        // sampling window, so identical content detected or not based on placement.
        string early = HomeIcon + Repeat("ordinary text ", 60);
        string late = Repeat("ordinary text ", 60) + HomeIcon;

        var utf8 = new UTF8Encoding(false);

        Assert.Equal("utf-8", Detect(early, utf8));
        Assert.Equal("utf-8", Detect(late, utf8));
        Assert.Equal(Detect(early, utf8), Detect(late, utf8));
    }

    [Fact]
    public void MostlyPrivateUseContent_IsStillRejected()
    {
        // The binary evidence the check exists to find must survive the change.
        Assert.Null(TextEncoding.DetectFromBuffer(
            new UTF8Encoding(false).GetBytes(Repeat(FirstPua, 400))));
    }

    [Fact]
    public void PrivateUseUpToTheThreshold_IsAccepted()
    {
        // 50 private-use scalars in 500 leaves a printable ratio of exactly 0.90, the
        // minimum the validator accepts.
        string text = Repeat(FirstPua + Repeat("a", 9), 50);

        Assert.Equal(500, text.EnumerateRunes().Count());
        Assert.Equal("utf-8", Detect(text, new UTF8Encoding(false)));
    }

    [Fact]
    public void PrivateUsePastTheThreshold_IsRejected()
    {
        // One in four is well past 10% non-printable.
        string text = Repeat(FirstPua + "abc", 200);

        Assert.Null(TextEncoding.DetectFromBuffer(
            new UTF8Encoding(false).GetBytes(text)));
    }

    [Fact]
    public void PrivateUseAndControlCharacters_ShareTheSameBudget()
    {
        // Both are excluded from the printable count, so they accumulate against one
        // threshold rather than each getting their own allowance.
        string text = Repeat(FirstPua + "" + Repeat("a", 8), 50);

        Assert.Null(TextEncoding.DetectFromBuffer(
            new UTF8Encoding(false).GetBytes(text)));
    }

    [Fact]
    public void PrivateUseText_ConvertsRatherThanBeingSkipped()
    {
        // End to end: the practical consequence of the old behaviour was that these
        // files could never be converted at all.
        string root = Directory.CreateTempSubdirectory("ec_pua_convert_").FullName;

        try
        {
            string path = Path.Combine(root, "icons.html");
            string content = $"<i class=\"icon\">{HomeIcon}</i> Home\n";
            File.WriteAllText(path, content, new UTF8Encoding(false));

            var options = new ScanDirectoryOptions
            {
                BaseDirectory = root,
                IncludePatterns = ["*.html"],
                Action = ScanAction.Convert,
                TargetCharset = "utf-8",
                TargetWriteBom = true,
            };

            var entries = new EntrySink();
            ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

            ConversionReportEntry entry = Assert.Single(entries);
            Assert.Equal(ConversionRowResult.Converted, entry.Result);
            Assert.Equal("utf-8", entry.SourceEncoding);

            byte[] converted = File.ReadAllBytes(path);
            Assert.Equal([0xEF, 0xBB, 0xBF], converted[..3]);
            Assert.Equal(content, new UTF8Encoding(true).GetString(converted[3..]));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void BinaryLikeBuffers_AreStillRejected()
    {
        // Guards the trade directly: relaxing the private-use rule must not let
        // binary through. Real PE/PNG/TTF files are covered by the corpus run; these
        // are the synthetic shapes closest to the relaxed rule.
        var random = new Random(4242);

        byte[] randomBytes = new byte[4096];
        random.NextBytes(randomBytes);
        Assert.Null(TextEncoding.DetectFromBuffer(randomBytes));

        // A UTF-16 buffer that decodes largely into the private-use plane.
        var puaHeavy = new StringBuilder();
        for (int i = 0; i < 600; i++)
            puaHeavy.Append((char)(0xE000 + (i % 0x1800)));

        Assert.Null(TextEncoding.DetectFromBuffer(
            new UnicodeEncoding(false, false).GetBytes(puaHeavy.ToString())));
    }
}
