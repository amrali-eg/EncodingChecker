using System;
using System.IO;
using System.Text;

namespace EncodingChecker;

internal static partial class EncodingConverter
{
    #region File I/O

    /// <summary>
    /// Consumes the encoding preamble if present; otherwise restores the original position.
    /// </summary>
    private static int ConsumePreambleIfPresent(
        FileStream stream,
        Encoding encoding)
    {
        byte[] preamble = encoding.GetPreamble();

        if (preamble.Length == 0)
            return 0;

        long startPosition = stream.Position;
        byte[] header = new byte[preamble.Length];

        int totalRead =
            stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);

        if (totalRead == preamble.Length &&
            header.AsSpan(0, totalRead).SequenceEqual(preamble))
        {
            return preamble.Length;
        }

        stream.Position = startPosition;
        return 0;
    }

    /// <summary>
    /// Opens a file for shared reads while blocking writes and deletion.
    /// </summary>
    private static FileStream OpenReadShared(string path, int bufferSize)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize,
            FileOptions.SequentialScan);
    }

    /// <summary>
    /// Creates a unique temp-file path beside the destination.
    /// </summary>
    private static string CreateTempFilePath(string destinationPath)
    {
        string fullDestination = Path.GetFullPath(destinationPath);
        string? directory = Path.GetDirectoryName(fullDestination);

        if (string.IsNullOrEmpty(directory))
        {
            throw new IOException(
                $"Could not determine a directory for destination path " +
                $"'{destinationPath}'.");
        }

        string fileName = Path.GetFileName(fullDestination);

        return Path.Combine(
            directory,
            $"{fileName}.{Guid.NewGuid():N}.{TempFileSuffix}");
    }

    /// <summary>
    /// Creates the temp file exclusively.
    /// </summary>
    private static FileStream CreateTempStream(string tempPath, int bufferSize)
    {
        return new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            FileOptions.SequentialScan);
    }

    /// <summary>
    /// Deletes the temp file if it still exists.
    /// </summary>
    private static void TryDeleteTempFile(string? tempPath)
    {
        if (tempPath is null)
            return;

        try
        {
            if (File.Exists(tempPath))
            {
                File.SetAttributes(tempPath, FileAttributes.Normal);
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Cleanup failure does not change the conversion outcome.
        }
    }

    #endregion

    #region Destination Handling

    /// <summary>
    /// Checks normalized path equality; does not resolve filesystem aliases such as hard links.
    /// </summary>
    private static bool IsSameFile(
        string sourcePath,
        string destinationPath)
    {
        string fullSource = Path.GetFullPath(sourcePath);
        string fullDestination = Path.GetFullPath(destinationPath);

        StringComparison comparison =
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        return string.Equals(fullSource, fullDestination, comparison);
    }

    /// <summary>Returns true for symbolic links and other reparse points.</summary>
    private static bool IsReparsePoint(FileInfo info)
    {
        return info.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
               info.LinkTarget is not null;
    }

    /// <summary>
    /// Applies requested source timestamps and attributes to the temp file before installation.
    /// </summary>
    /// <remarks>
    /// Preserves only the attributes and timestamps covered by the converter's contract.
    /// </remarks>
    /// <returns>
    /// <see langword="null"/> on success; otherwise a failure description.
    /// </returns>
    private static string? RestoreTempFileMetadata(
        string tempPath,
        ConversionOptions options,
        FileAttributes capturedAttributes,
        DateTime capturedCreationTimeUtc,
        DateTime capturedLastWriteTimeUtc,
        DateTime capturedLastAccessTimeUtc)
    {
        if (options.PreserveTimestamps)
        {
            try
            {
                File.SetCreationTimeUtc(tempPath, capturedCreationTimeUtc);
                File.SetLastWriteTimeUtc(tempPath, capturedLastWriteTimeUtc);
                File.SetLastAccessTimeUtc(tempPath, capturedLastAccessTimeUtc);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException
                    or ArgumentException or PlatformNotSupportedException)
            {
                return $"Timestamp restoration failed: {ex.Message}";
            }
        }

        if (options.PreserveAttributes)
        {
            try
            {
                File.SetAttributes(tempPath, capturedAttributes);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException
                    or ArgumentException or PlatformNotSupportedException)
            {
                return $"Attribute restoration failed: {ex.Message}";
            }
        }

        return null;
    }

    /// <summary>
    /// Installs the verified temp file, using atomic replacement when available.
    /// </summary>
    /// <param name="metadataRestoreFailed">Indicates whether destination metadata restoration failed.</param>
    /// <param name="replacementCommitted">
    /// True if replacement occurred, false if it did not, or null if the outcome is unknown.
    /// </param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="sameFile">Whether source and destination are the same file.</param>
    /// <param name="tempPath">The verified temporary file.</param>
    /// <param name="errorMessage">A message describing the error, if any.</param>
    /// <returns><see langword="null"/> on success; otherwise the failure code.</returns>
    private static ConversionErrorCode? AtomicReplace(
        string tempPath,
        string destinationPath,
        bool sameFile,
        out string? errorMessage,
        out bool metadataRestoreFailed,
        out bool? replacementCommitted)
    {
        errorMessage = null;
        metadataRestoreFailed = false;
        replacementCommitted = false;

        try
        {
            FileInfo destinationInfo = new(destinationPath);
            bool destinationExists = destinationInfo.Exists;

            // Never replace a reparse-point destination.
            if (destinationExists && IsReparsePoint(destinationInfo))
            {
                errorMessage =
                    "The destination is a symbolic link or other reparse point; " +
                    "replacement was rejected.";
                return ConversionErrorCode.ReparsePointRejected;
            }

            // Temporarily clear ReadOnly so replacement can proceed.
            FileAttributes? clearedAttributes =
                destinationExists &&
                destinationInfo.Attributes.HasFlag(FileAttributes.ReadOnly)
                    ? destinationInfo.Attributes
                    : null;

            try
            {
                if (clearedAttributes is not null)
                {
                    File.SetAttributes(
                        destinationPath,
                        clearedAttributes.Value & ~FileAttributes.ReadOnly);
                }

                ReplaceOrMove(tempPath, destinationPath, destinationExists);
            }
            catch (Exception replaceEx) when (
                replaceEx is IOException or UnauthorizedAccessException
                    or ArgumentException or NotSupportedException)
            {
                bool tempStillExists = File.Exists(tempPath);
                string? rollbackError = null;

                if (tempStillExists)
                {
                    // Replacement did not consume the temp file, so restore the original state.
                    if (clearedAttributes is not null)
                    {
                        try
                        {
                            File.SetAttributes(
                                destinationPath,
                                clearedAttributes.Value);
                        }
                        catch (Exception rollbackEx)
                        {
                            rollbackError =
                                $" The original ReadOnly attribute could also not be " +
                                $"restored: {rollbackEx.Message}";
                        }
                    }

                    replacementCommitted = false;
                    errorMessage =
                        $"Failed to install the converted file: {replaceEx.Message}" +
                        rollbackError;
                }
                else
                {
                    // The destination state cannot be determined reliably.
                    replacementCommitted = null;
                    errorMessage =
                        $"Failed to install the converted file: {replaceEx.Message} " +
                        "The temporary file was no longer present afterward, so it is " +
                        "unclear whether the destination was actually replaced despite " +
                        "this error — inspect the destination directly if that matters.";
                }

                return ConversionErrorCode.ReplacementError;
            }

            replacementCommitted = true;

            // Restore the destination's original ReadOnly state when needed.
            if (clearedAttributes is not null)
            {
                try
                {
                    if (!sameFile ||
                        !File.GetAttributes(destinationPath)
                            .HasFlag(FileAttributes.ReadOnly))
                    {
                        File.SetAttributes(
                            destinationPath,
                            clearedAttributes.Value);
                    }
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException
                        or ArgumentException or NotSupportedException)
                {
                    metadataRestoreFailed = true;
                    errorMessage =
                        $"Destination attribute restoration failed: {ex.Message}";
                }
            }

            return null;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException)
        {
            errorMessage = $"Failed to install the converted file: {ex.Message}";
            return ConversionErrorCode.ReplacementError;
        }
    }

    /// <summary>
    /// Replaces <paramref name="destinationPath"/> with <paramref name="tempPath"/> using
    /// <see cref="File.Replace(string, string, string?, bool)"/> when supported, otherwise
    /// using a move.
    /// </summary>
    private static void ReplaceOrMove(
        string tempPath,
        string destinationPath,
        bool destinationExists)
    {
        if (destinationExists)
        {
            try
            {
                // Filesystem metadata outside the converter's contract need not be preserved.
                File.Replace(
                    tempPath,
                    destinationPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            catch (PlatformNotSupportedException)
            {
                // The fallback move is not guaranteed to be atomic.
                File.Move(tempPath, destinationPath, overwrite: true);
            }
        }
        else
        {
            File.Move(tempPath, destinationPath);
        }
    }

    /// <summary>
    /// Atomically installs a completed backup and restores the destination's ReadOnly state.
    /// </summary>
    internal static void AtomicReplaceForBackup(string tempPath, string destinationPath)
    {
        var destinationInfo = new FileInfo(destinationPath);
        bool destinationExists = destinationInfo.Exists;

        FileAttributes? clearedAttributes =
            destinationExists && destinationInfo.Attributes.HasFlag(FileAttributes.ReadOnly)
                ? destinationInfo.Attributes
                : null;

        if (clearedAttributes is not null)
        {
            File.SetAttributes(
                destinationPath,
                clearedAttributes.Value & ~FileAttributes.ReadOnly);
        }

        try
        {
            ReplaceOrMove(tempPath, destinationPath, destinationExists);
        }
        catch (Exception replaceEx) when (
            replaceEx is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException)
        {
            string? rollbackError = TryRestoreReadOnly(destinationPath, clearedAttributes);

            if (rollbackError is null)
                throw;

            throw new IOException(
                $"{replaceEx.Message} The original ReadOnly attribute could also not " +
                $"be restored: {rollbackError}",
                replaceEx);
        }

        string? restoreError = TryRestoreReadOnly(destinationPath, clearedAttributes);

        if (restoreError is not null)
        {
            throw new IOException(
                $"The backup was written, but the original ReadOnly attribute could " +
                $"not be restored: {restoreError}");
        }
    }


    /// <summary>
    /// Restores a previously cleared ReadOnly attribute.
    /// </summary>
    private static string? TryRestoreReadOnly(
        string destinationPath,
        FileAttributes? clearedAttributes)
    {
        if (clearedAttributes is null)
            return null;

        try
        {
            if (File.Exists(destinationPath))
                File.SetAttributes(destinationPath, clearedAttributes.Value);

            return null;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException)
        {
            return ex.Message;
        }
    }

    #endregion
}
