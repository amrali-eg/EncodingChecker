using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// The destination's ReadOnly attribute is cleared so replacement can proceed and must be
/// put back afterwards. Restoration used to be guarded by !sameFile, which is only safe
/// while the temp file arrives already carrying the original attributes - something
/// PreserveAttributes = false skips, silently dropping ReadOnly.
///
/// Targets EncodingConverter directly: the product always leaves PreserveAttributes at
/// its default, so the defect is reachable only through the library API.
/// </summary>
public sealed class ReadOnlyPreservationMatrixTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_readonly_matrix_").FullName;

    public void Dispose()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    private string WriteFile(string name, string content, bool readOnly)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));

        if (readOnly)
            File.SetAttributes(path, FileAttributes.ReadOnly);

        return path;
    }

    private static ConversionResult Convert(
        string sourcePath,
        string destinationPath,
        bool preserveAttributes) =>
        EncodingConverter.Convert(
            sourcePath,
            destinationPath,
            new UTF8Encoding(false),
            new UnicodeEncoding(false, false),
            new ConversionOptions
            {
                WriteBom = false,
                PreserveAttributes = preserveAttributes,
            },
            progress: null,
            CancellationToken.None);

    // ---- same-file replacement ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameFile_ReadOnly_StaysReadOnly_RegardlessOfPreserveAttributes(
        bool preserveAttributes)
    {
        // The regression: with preserveAttributes false the temp carries no ReadOnly and
        // the !sameFile guard skipped restoration, so the bit vanished.
        string path = WriteFile(
            $"same_ro_{preserveAttributes}.txt", "café content", readOnly: true);

        ConversionResult result = Convert(path, path, preserveAttributes);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(
            File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly),
            "ReadOnly must survive a same-file conversion.");

        Assert.Equal(
            "café content",
            new UnicodeEncoding(false, false).GetString(File.ReadAllBytes(path)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameFile_NotReadOnly_StaysWritable(bool preserveAttributes)
    {
        string path = WriteFile(
            $"same_rw_{preserveAttributes}.txt", "plain content", readOnly: false);

        ConversionResult result = Convert(path, path, preserveAttributes);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.False(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
    }

    // ---- different destination ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DifferentDestination_ReadOnlyDestination_StaysReadOnly(
        bool preserveAttributes)
    {
        string source = WriteFile(
            $"src_{preserveAttributes}.txt", "source content", readOnly: false);

        string destination = WriteFile(
            $"dst_ro_{preserveAttributes}.txt", "old destination", readOnly: true);

        ConversionResult result = Convert(source, destination, preserveAttributes);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.GetAttributes(destination).HasFlag(FileAttributes.ReadOnly));

        Assert.Equal(
            "source content",
            new UnicodeEncoding(false, false).GetString(File.ReadAllBytes(destination)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DifferentDestination_WritableDestination_StaysWritable(
        bool preserveAttributes)
    {
        string source = WriteFile(
            $"src2_{preserveAttributes}.txt", "source content", readOnly: false);

        string destination = WriteFile(
            $"dst_rw_{preserveAttributes}.txt", "old destination", readOnly: false);

        ConversionResult result = Convert(source, destination, preserveAttributes);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.False(File.GetAttributes(destination).HasFlag(FileAttributes.ReadOnly));
    }

    // ---- failure path ----

    [Fact]
    public void FailedConversion_LeavesTheReadOnlyDestinationUntouched()
    {
        // Cyrillic cannot be represented in windows-1252, so verification rejects the
        // write and nothing is installed.
        string path = Path.Combine(_root, "unmappable.txt");
        File.WriteAllText(path, "Привет", new UTF8Encoding(false));
        byte[] originalBytes = File.ReadAllBytes(path);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        ConversionResult result = EncodingConverter.Convert(
            path, path,
            new UTF8Encoding(false),
            Encoding.GetEncoding("windows-1252"),
            new ConversionOptions { WriteBom = false },
            progress: null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(
            File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly),
            "A failed conversion must not strip ReadOnly from the original.");

        Assert.Equal(originalBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void SameFile_PreserveAttributesFalse_DoesNotResurrectUnrelatedAttributes()
    {
        // Restoration puts back exactly the destination's captured attribute set; a file
        // that was never Hidden must not become Hidden.
        string path = WriteFile("same_plain.txt", "content here", readOnly: true);

        ConversionResult result = Convert(path, path, preserveAttributes: false);

        Assert.True(result.Success, result.ErrorMessage);

        FileAttributes attributes = File.GetAttributes(path);
        Assert.True(attributes.HasFlag(FileAttributes.ReadOnly));
        Assert.False(attributes.HasFlag(FileAttributes.Hidden));
        Assert.False(attributes.HasFlag(FileAttributes.System));
    }
}
