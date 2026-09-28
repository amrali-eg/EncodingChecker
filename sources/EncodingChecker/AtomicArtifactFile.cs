using System;
using System.IO;
using System.Text;

namespace EncodingChecker;

/// <summary>
/// Writes plans, journals, reports, and settings without replacing the previous
/// artifact until its successor is complete.
/// </summary>
/// <remarks>
/// Recovery sidecars keep their own writer because it also reads back and validates
/// each record. A read-only or linked destination is refused rather than replaced; see
/// <see cref="RefusalForExistingDestination"/>.
/// </remarks>
internal static class AtomicArtifactFile
{
    /// <summary>Writes to a temporary file, then installs the completed artifact.</summary>
    /// <returns><see langword="null"/> on success; otherwise, a diagnostic.</returns>
    internal static string? Write(string path, Action<Stream> writeContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(writeContent);

        string fullPath = Path.GetFullPath(path);

        if (RefusalForExistingDestination(fullPath) is { } refusal)
            return refusal;

        // Keep the temporary file on the destination volume and under a suffix scans ignore.
        string tempPath =
            $"{fullPath}.{Guid.NewGuid():N}.{EncodingConverter.TempFileSuffix}";

        try
        {
            using (var stream = new FileStream(
                       tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                writeContent(stream);

                // Flush to disk before installation so a power loss cannot expose an empty file.
                stream.Flush(flushToDisk: true);
            }

            EncodingConverter.AtomicReplaceForBackup(tempPath, fullPath);
            return null;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException or InvalidOperationException)
        {
            return ex.Message;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Cleanup failure cannot invalidate an artifact already installed.
            }
        }
    }

    /// <summary>
    /// Why an existing destination must not be replaced, or <see langword="null"/> when it
    /// may be.
    /// </summary>
    /// <remarks>
    /// Installing the staged file clears a read-only flag and swaps a link for a regular
    /// file, where a direct write would have failed or written through the link. Refusing
    /// keeps the existing file and what its owner meant by marking or linking it. The CLI
    /// also calls this before any file changes, so the refusal comes before conversion.
    /// </remarks>
    internal static string? RefusalForExistingDestination(string path)
    {
        FileAttributes attributes;

        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException)
        {
            // Not there yet, or not readable; the write reports its own failure.
            return null;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
            return $"'{path}' is a link. Choose a regular file instead.";

        if ((attributes & FileAttributes.ReadOnly) != 0)
            return $"'{path}' is read-only.";

        return null;
    }

    /// <summary>
    /// Writes text, including any preamble required by <paramref name="encoding"/>.
    /// </summary>
    internal static string? WriteText(string path, string content, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(encoding);

        return Write(path, stream =>
        {
            using var writer = new StreamWriter(stream, encoding, leaveOpen: true);
            writer.Write(content);
        });
    }
}
