namespace EncodingChecker.Tests;

/// <summary>Shared text fixtures for conversion tests.</summary>
internal static class TestContent
{
    /// <summary>Plain ASCII text, representable in every encoding under test, including ASCII itself.</summary>
    internal const string Ascii = "Hello, World! 123\r\nSecond line here.\r\n";

    /// <summary>
    /// Multi-script text spanning several Unicode blocks (Latin, CJK, Arabic, Cyrillic, Greek,
    /// Hebrew, Hangul) plus an astral-plane emoji requiring a UTF-16 surrogate pair, so
    /// conversions are verified against more than Latin-script content.
    /// </summary>
    internal const string Multilingual =
        "Hello 世界 مرحبا بالعالم Привет мир Γειά σου κόσμε " +
        "שלום עולם 안녕하세요 세계 🌍🎉\r\nSecond line here.\r\n";
}
