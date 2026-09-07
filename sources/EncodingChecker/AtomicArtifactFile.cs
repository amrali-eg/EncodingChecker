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
/// each record.
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
