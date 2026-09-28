using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// The window's Validate status tells "every file passed" apart from "no file was checked".
/// </summary>
/// <remarks>
/// Files that pass validation get no row, so the status built from the row count read
/// "0 files do not have the correct encoding" both when every file passed and when a mistyped
/// mask matched nothing. The cases feed a real Validate scan into the tally the way the window
/// does, then read the status the window shows.
/// </remarks>
public sealed class ValidationStatusTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_validate_status_").FullName;

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

    private string Validate(string mask = "*")
    {
        var tally = new MainForm.ValidationTally();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                IncludePatterns = [mask],
                Action = ScanAction.Validate,
                ValidCharsets = ["utf-8"],
            },
            entry => tally.Count(entry.Result),
            CancellationToken.None);

        return tally.Describe();
    }

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    [Fact]
    public void AMaskThatMatchesNothingSaysNoFileWasExamined()
    {
        Write("a.txt", Utf8("valid 世界\n"));

        Assert.Equal("No matching files were examined", Validate(mask: "*.tx"));
    }

    [Fact]
    public void AnEmptyFolderSaysNoFileWasExamined()
    {
        Assert.Equal("No matching files were examined", Validate());
    }

    [Fact]
    public void FilesThatAllPassSayHowManyWereChecked()
    {
        Write("a.txt", Utf8("valid 世界\n"));
        Write("b.txt", Utf8("also valid\n"));

        Assert.Equal("Checked 2 files: all valid", Validate());
    }

    [Fact]
    public void InvalidAndUnreadableFilesAreCountedSeparately()
    {
        Write("valid.txt", Utf8("valid 世界\n"));
        Write("legacy.txt", Encoding.GetEncoding("windows-1252").GetBytes("Le café était déjà prêt"));
        string locked = Write("locked.txt", Utf8("held open\n"));

        string status;

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            status = Validate();

        Assert.Equal(
            "Checked 3 files: 1 do not have the correct encoding, 1 could not be read",
            status);
    }

    [Fact]
    public void InvalidFilesAloneKeepTheFamiliarWording()
    {
        Write("valid.txt", Utf8("valid 世界\n"));
        Write("legacy.txt", Encoding.GetEncoding("windows-1252").GetBytes("Le café était déjà prêt"));

        Assert.Equal("Checked 2 files: 1 do not have the correct encoding", Validate());
    }
}
