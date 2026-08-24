using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// AtomicReplaceForBackup clears the destination's ReadOnly attribute so the replacement
/// can proceed, then puts it back. Restoring from a plain finally meant a failing
/// SetAttributes could throw over an in-flight replacement failure, so the caller was
/// told about an attribute problem instead of why the backup actually failed.
///
/// SCOPE: these pin the observable contract - the replacement failure is what surfaces,
/// the rollback still happens, and the success path is unchanged. They do NOT exercise
/// the masking branch itself, which needs the replacement AND the rollback to fail
/// together. That combination is not reachable from the filesystem: SetAttributes
/// succeeds on a locked file and silently ignores unsettable attributes, and any ACL
/// that would deny the rollback also denies the initial clear, which happens earlier and
/// outside the try. Forcing it would need an injection seam in production code, so the
/// branch is covered by inspection against AtomicReplace's equivalent handling instead.
/// </summary>
public sealed class BackupExceptionPreservationTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_backup_exc_").FullName;

    public void Dispose()
    {
        try
        {
            // ReadOnly fixtures would otherwise block the recursive delete.
            foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    private string WriteFile(string name, string content, bool readOnly = false)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));

        if (readOnly)
            File.SetAttributes(path, FileAttributes.ReadOnly);

        return path;
    }

    [Fact]
    public void ReplacementFailure_IsReportedInsteadOfAnAttributeError()
    {
        // A missing temp file makes the replacement fail deterministically while the
        // destination stays present and restorable - the exact shape where a finally
        // could previously substitute its own exception.
        string missingTemp = Path.Combine(_root, "does-not-exist.tmp");
        string destination = WriteFile("target.bak", "original", readOnly: true);

        var ex = Assert.ThrowsAny<IOException>(
            () => EncodingConverter.AtomicReplaceForBackup(missingTemp, destination));

        // The reported failure must describe the replacement, not the attribute rollback.
        Assert.DoesNotContain(
            "ReadOnly attribute could also not be restored",
            ex.Message,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "could not be restored",
            ex.Message,
            StringComparison.Ordinal);

        // And the rollback still happened: the destination keeps its ReadOnly state.
        Assert.True(File.GetAttributes(destination).HasFlag(FileAttributes.ReadOnly));
        Assert.Equal("original", File.ReadAllText(destination));
    }

    [Fact]
    public void ReplacementFailure_PreservesTheOriginalExceptionType()
    {
        string missingTemp = Path.Combine(_root, "absent.tmp");
        string destination = WriteFile("target2.bak", "original", readOnly: true);

        var ex = Assert.ThrowsAny<IOException>(
            () => EncodingConverter.AtomicReplaceForBackup(missingTemp, destination));

        // Rethrown unchanged when the rollback succeeds - not wrapped, not replaced.
        Assert.IsType<FileNotFoundException>(ex);
    }

    [Fact]
    public void SuccessfulReplacement_InstallsTheTempAndRestoresReadOnly()
    {
        string temp = WriteFile("incoming.tmp", "new backup content");
        string destination = WriteFile("target3.bak", "stale backup", readOnly: true);

        EncodingConverter.AtomicReplaceForBackup(temp, destination);

        Assert.Equal("new backup content", File.ReadAllText(destination));
        Assert.True(File.GetAttributes(destination).HasFlag(FileAttributes.ReadOnly));
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public void SuccessfulReplacement_OverANonReadOnlyDestination_LeavesItWritable()
    {
        string temp = WriteFile("incoming2.tmp", "fresh");
        string destination = WriteFile("target4.bak", "stale");

        EncodingConverter.AtomicReplaceForBackup(temp, destination);

        Assert.Equal("fresh", File.ReadAllText(destination));
        Assert.False(File.GetAttributes(destination).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void NewBackup_WithNoExistingDestination_Succeeds()
    {
        string temp = WriteFile("incoming3.tmp", "first backup");
        string destination = Path.Combine(_root, "brand-new.bak");

        EncodingConverter.AtomicReplaceForBackup(temp, destination);

        Assert.Equal("first backup", File.ReadAllText(destination));
    }
}
