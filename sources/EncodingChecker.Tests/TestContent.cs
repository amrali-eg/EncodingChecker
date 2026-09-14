using System.IO;
using System.Linq;
using System.Text;

namespace EncodingChecker.Tests;

/// <summary>Shared text fixtures for conversion tests.</summary>
internal static class TestContent
{
    /// <summary>Writes <paramref name="text"/> under <paramref name="root"/>, encoded as <paramref name="charset"/>.</summary>
    internal static string Write(string root, string name, string text, string charset)
    {
        string path = Path.Combine(root, name);
        File.WriteAllBytes(path, Encoding.GetEncoding(charset).GetBytes(text));
        return path;
    }

    /// <summary>Whether the file on disk still begins with a UTF-8 BOM.</summary>
    internal static bool StillHasBom(string path) =>
        File.ReadAllBytes(path).Take(3).SequenceEqual(Encoding.UTF8.GetPreamble());

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
