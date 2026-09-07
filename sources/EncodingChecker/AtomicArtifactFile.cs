using System;
using System.IO;
using System.Text;

namespace EncodingChecker;

/// <summary>
/// Writes one of EC's own artifacts so that a failure leaves the previous version intact.
/// </summary>
/// <remarks>
/// EC installs a converted file by writing a temporary file beside it and replacing the
/// original, and it writes its recovery sidecar the same way. Its plan, journal, report
/// and settings did not: each truncated its destination and then wrote into it, so an
/// interruption left a half-written artifact where a readable one had been. The plan is
/// the worst of the four - a truncated one destroys the reviewed plan a user was about to
/// apply - and the settings file is the one already known to have failed this way,
/// recorded as EC-16.
///
/// The machinery to avoid this already existed and already ships; this routes the
/// remaining four through it rather than adding a fifth way to save a file.
/// </remarks>
internal static class AtomicArtifactFile
{
    /// <summary>
    /// Writes what <paramref name="writeContent"/> produces to <paramref name="path"/>,
    /// installing it only once it is complete.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the artifact is installed, or the message to report.
    /// </returns>
    internal static string? Write(string path, Action<Stream> writeContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(writeContent);

        string fullPath = Path.GetFullPath(path);

        // Beside the destination, so the replacement stays within one volume, and under
        // the suffix a scan already excludes, so a leftover cannot become a scan
        // candidate.
        string tempPath =
            $"{fullPath}.{Guid.NewGuid():N}.{EncodingConverter.TempFileSuffix}";

        try
        {
            using (var stream = new FileStream(
                       tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                writeContent(stream);

                // Renaming a file whose contents are still only in the page cache would
                // make the install atomic and the artifact empty after a power loss.
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
                // A leftover temporary file cannot make the installed artifact wrong.
            }
        }
    }

    /// <summary>Writes <paramref name="content"/> in <paramref name="encoding"/>.</summary>
    /// <remarks>
    /// The encoding's preamble is written, because the stream is new and positioned at
    /// zero - matching what <see cref="StreamWriter"/> did when it owned the destination.
    /// </remarks>
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
