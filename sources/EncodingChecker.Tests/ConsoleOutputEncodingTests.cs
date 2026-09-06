using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// Standard output must be encoded for whoever is going to read it.
/// </summary>
/// <remarks>
/// After attaching to the parent console, both writers were rebuilt with
/// <c>StreamWriter</c>'s default encoding, which is UTF-8 whatever the console is. On a
/// CP437 console — the default on the machine this was found on — the per-file CSV rendered
/// "Gr&#252;&#223;e aus M&#252;nchen" as "Gr&#9500;&#9565;&#9500;&#402;e aus M&#9500;&#9565;nchen": the tool's own output exhibiting the
/// defect it exists to detect.
/// </remarks>
public sealed class ConsoleOutputEncodingTests
{
    private static byte[] Write(bool redirected, string text)
    {
        using var buffer = new MemoryStream();

        using (StreamWriter writer = Program.OpenStandardWriter(buffer, redirected))
            writer.Write(text);

        return buffer.ToArray();
    }

    [Fact]
    public void RedirectedOutputIsUtf8WithoutABom()
    {
        // A file or a pipe wants the encoding the -Report file uses, minus its BOM.
        byte[] written = Write(redirected: true, "Gr\u00fc\u00dfe");

        Assert.Equal(new UTF8Encoding(false).GetBytes("Gr\u00fc\u00dfe"), written);
    }

    [Fact]
    public void ConsoleOutputUsesTheConsoleEncoding()
    {
        // Whatever the console decodes with is what it must be handed.
        byte[] written = Write(redirected: false, "Gr\u00fc\u00dfe");

        Assert.Equal(Console.OutputEncoding.GetBytes("Gr\u00fc\u00dfe"), written);
    }

    [Fact]
    public void NeitherWriterEmitsAPreamble()
    {
        // A BOM in the middle of a shell session, or at the head of a piped CSV, would be
        // a new defect rather than a fix.
        Assert.Empty(Write(redirected: true, string.Empty));
        Assert.Empty(Write(redirected: false, string.Empty));
    }

    [Fact]
    public void AsciiIsIdenticalEitherWay()
    {
        // The common case must not depend on which branch ran.
        Assert.Equal(
            Write(redirected: true, "File,Encoding,BOM"),
            Write(redirected: false, "File,Encoding,BOM"));
    }
}
