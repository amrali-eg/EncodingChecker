using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace EncodingChecker;

#region Public Types

/// <summary>Machine-readable conversion failure code.</summary>
internal enum ConversionErrorCode
{
    None = 0,
    SourceOpenError,
    SourceReadError,
    SourceDecodeError,
    TargetEncodeError,
    TargetWriteError,
    TargetReadError,
    TargetDecodeError,
    VerificationFailed,
    UnicodeMismatch,
    BomMismatch,
    MultipleLeadingByteOrderMarks,
    TemporaryFileError,
    ReplacementError,
    RecoveryRecordError,
    MetadataRestoreFailed,
    CodePageProviderNotRegistered,
    ReparsePointRejected,
    SourceChangedDuringConversion,
    Cancelled,
    Unexpected,
}

/// <summary>Options controlling <see cref="EncodingConverter.Convert"/>.</summary>
internal sealed record ConversionOptions
{
    /// <summary>Default conversion options.</summary>
    internal static readonly ConversionOptions Default = new();

    /// <summary>Streaming buffer size in bytes.</summary>
    internal int BufferSize { get; init; } = EncodingConverter.DefaultBufferSize;

    /// <summary>
    /// Whether to write a BOM. Requires the target encoding to support a preamble.
    /// </summary>
    internal bool WriteBom { get; init; }

    /// <summary>
    /// Whether to preserve source attributes during in-place conversion.
    /// Ignored when writing to a different destination. Defaults to <see langword="true"/>.
    /// </summary>
    internal bool PreserveAttributes { get; init; } = true;

    /// <summary>
    /// Whether to preserve source timestamps during in-place conversion.
    /// Ignored for a different destination. Defaults to <see langword="false"/>.
    /// </summary>
    internal bool PreserveTimestamps { get; init; }

    /// <summary>
    /// Invoked after verification and before installation to record how the conversion
    /// can be undone. Returns <see langword="null"/> on success.
    /// </summary>
    /// <remarks>
    /// Kept inside the safety boundary so a recording failure leaves the original intact.
    /// </remarks>
    internal Func<ConversionRecord, string?>? RecordConversion { get; init; }

    /// <summary>
    /// Marks a prepared conversion record complete after installation.
    /// Returns <see langword="null"/> on success.
    /// </summary>
    internal Func<string?>? CompleteConversionRecord { get; init; }

    /// <summary>
    /// The SHA-256 the source must still have at installation time, or
    /// <see langword="null"/> to skip the check.
    /// </summary>
    /// <remarks>
    /// Used by an approved plan to ensure the file has not changed since it was reviewed.
    /// </remarks>
    internal string? ExpectedSourceSha256 { get; init; }
}

/// <summary>
/// Everything needed to reverse or independently reconstruct one conversion.
/// </summary>
internal sealed record ConversionRecord
{
    internal required string SourcePath { get; init; }

    internal required long SourceBytes { get; init; }

    /// <summary>SHA-256 of the source file's bytes, before conversion.</summary>
    internal required string SourceSha256 { get; init; }

    /// <summary>SHA-256 over the decoded source text, independent of encoding.</summary>
    internal required string SourceTextSha256 { get; init; }

    /// <summary>
    /// SHA-256 over the decoded converted text. Equal to the source text hash after
    /// successful verification.
    /// </summary>
    internal required string OutputTextSha256 { get; init; }

    /// <summary>SHA-256 of the verified output file's exact bytes.</summary>
    internal required string OutputSha256 { get; init; }

    internal required string SourceEncoding { get; init; }

    /// <summary>
    /// The code page identifying the source codec independently of its alias names.
    /// </summary>
    internal required int SourceCodePage { get; init; }

    internal required bool SourceHasBom { get; init; }

    internal required string TargetEncoding { get; init; }

    internal required int TargetCodePage { get; init; }

    internal required bool TargetHasBom { get; init; }

    internal required long UnicodeScalars { get; init; }
}

/// <summary>Progress reported by bytes processed.</summary>
internal sealed record ConversionProgress
{
    internal long BytesProcessed { get; init; }

    internal long TotalBytes { get; init; }

    internal double Percentage { get; init; }
}

/// <summary>Structured result of an <see cref="EncodingConverter.Convert"/> call.</summary>
internal sealed record ConversionResult
{
    internal bool Success { get; init; }

    internal ConversionErrorCode ErrorCode { get; init; } = ConversionErrorCode.None;

    internal string? ErrorMessage { get; init; }

    internal Exception? Exception { get; init; }

    internal required Encoding SourceEncoding { get; init; }

    internal required Encoding TargetEncoding { get; init; }

    /// <summary>Source bytes actually processed.</summary>
    internal long SourceBytes { get; init; }

    /// <summary>Target bytes written, including any BOM.</summary>
    internal long TargetBytes { get; init; }

    internal long UnicodeScalarsVerified { get; init; }

    internal bool VerificationPassed { get; init; }

    internal bool BomVerificationPassed { get; init; }

    /// <summary>
    /// Whether replacement is known to have occurred. Null means the outcome is unknown.
    /// </summary>
    internal bool? ReplacementCommitted { get; init; }
}

#endregion

/// <summary>
/// Converts text files between encodings without data loss.
/// Verifies converted content before replacement.
/// </summary>
internal static partial class EncodingConverter
{
    #region Constants

    // Keep memory use bounded while still allowing efficient streaming.
    internal const int DefaultBufferSize = 256 * 1024;

    // Avoid flooding progress consumers with updates.
    private const long ProgressReportIntervalMs = 100;

    // Shared with DirectoryTraversal so temporary files are never scanned as input.
    internal const string TempFileSuffix = "unicodechecker.tmp";

    #endregion

    #region Public API

    /// <summary>
    /// Converts a file between encodings using a verified temporary file.
    /// Uses atomic replacement when supported; fallback replacement is not atomic.
    /// </summary>
    /// <param name="sourcePath">Source file path.</param>
    /// <param name="destinationPath">Destination path; may equal the source path.</param>
    /// <param name="sourceEncoding">Detected source encoding.</param>
    /// <param name="targetEncoding">Target encoding.</param>
    /// <param name="options">Conversion options.</param>
    /// <param name="progress">Optional progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A structured <see cref="ConversionResult"/>.</returns>
    internal static ConversionResult Convert(
        string sourcePath,
        string destinationPath,
        Encoding sourceEncoding,
        Encoding targetEncoding,
        ConversionOptions? options = null,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(destinationPath);
        ArgumentNullException.ThrowIfNull(sourceEncoding);
        ArgumentNullException.ThrowIfNull(targetEncoding);

        options ??= ConversionOptions.Default;

        if (options.BufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.BufferSize,
                @"ConversionOptions.BufferSize must be greater than zero.");
        }

        // Reject an impossible BOM request before touching the source.
        if (options.WriteBom && targetEncoding.GetPreamble().Length == 0)
        {
            return Failure(
                ConversionErrorCode.BomMismatch,
                $"The requested target encoding ({targetEncoding.EncodingName}) " +
                $"does not provide a byte-order mark, so the requested BOM " +
                $"policy (WriteBom = true) cannot be satisfied.",
                sourceEncoding,
                targetEncoding);
        }

        string? tempPath = null;

        long sourceBytesProcessed = 0;
        long targetBytesWritten = 0;

        // Captured for the conversion record after the source stream is closed.

        try
        {
            bool sameFile = IsSameFile(sourcePath, destinationPath);
            long capturedLength;
            DateTime capturedLastWriteUtc;
            DateTime capturedCreationTimeUtc;
            DateTime capturedLastAccessTimeUtc;
            FileAttributes capturedAttributes;
            int effectiveBufferSize;
            ContentDigest sourceDigest;

            // Open first so the metadata snapshot refers to the file actually held by the handle.
            bool sourceHadBom = false;
            using (FileStream sourceStream = RunIoStage(
                       ConversionErrorCode.SourceOpenError,
                       "Failed to open the source file",
                       () => OpenReadShared(sourcePath, options.BufferSize)))
            {
                FileInfo sourceInfo = new(sourcePath);

                if (IsReparsePoint(sourceInfo))
                {
                    return Failure(
                        ConversionErrorCode.ReparsePointRejected,
                        "The source is a symbolic link or other reparse point; " +
                        "conversion was rejected.",
                        sourceEncoding,
                        targetEncoding);
                }

                capturedLength = sourceInfo.Length;
                capturedLastWriteUtc = sourceInfo.LastWriteTimeUtc;
                capturedCreationTimeUtc = sourceInfo.CreationTimeUtc;
                capturedLastAccessTimeUtc = sourceInfo.LastAccessTimeUtc;
                capturedAttributes = sourceInfo.Attributes;

                // Limit the buffer to the source size while keeping it usable for empty files.
                effectiveBufferSize =
                    (int)Math.Max(1, Math.Min(options.BufferSize, capturedLength));

                tempPath = CreateTempFilePath(destinationPath);

                string path = tempPath;
                using FileStream tempStream = RunIoStage(
                    ConversionErrorCode.TemporaryFileError,
                    "Failed to create the temporary output file",
                    () => CreateTempStream(path, effectiveBufferSize));

                // Consume one source BOM as metadata; an additional one is ambiguous text.
                int sourcePreambleLength = RunIoStage(
                    ConversionErrorCode.SourceReadError,
                    "Failed to read the source file's leading bytes",
                    () => ConsumePreambleIfPresent(sourceStream, sourceEncoding));

                sourceBytesProcessed += sourcePreambleLength;
                sourceHadBom = sourcePreambleLength > 0;

                // A second marker would be decoded as U+FEFF text.  Do not discard it
                // automatically: it could be intentional content, and a no-BOM target
                // cannot represent that distinction reliably at the start of a file.
                if (sourceHadBom && HasPreambleAtCurrentPosition(sourceStream, sourceEncoding))
                {
                    return Failure(
                        ConversionErrorCode.MultipleLeadingByteOrderMarks,
                        $"The source begins with multiple {sourceEncoding.WebName} " +
                        "byte-order marks. No conversion was performed because an " +
                        "additional marker may be intentional text. Remove the duplicate " +
                        "marker manually, then try again.",
                        sourceEncoding,
                        targetEncoding);
                }

                if (options.WriteBom)
                {
                    byte[] preamble = targetEncoding.GetPreamble();

                    if (preamble.Length > 0)
                    {
                        tempStream.Write(preamble, 0, preamble.Length);
                        targetBytesWritten += preamble.Length;
                    }
                }

                Decoder sourceDecoder = MakeStrictDecoder(sourceEncoding);
                Encoder targetEncoder = MakeStrictEncoder(targetEncoding);

                sourceDigest = StreamConvert(
                    sourceStream,
                    tempStream,
                    sourceEncoding,
                    sourceDecoder,
                    targetEncoding,
                    targetEncoder,
                    effectiveBufferSize,
                    capturedLength,
                    progress,
                    cancellationToken,
                    ref sourceBytesProcessed,
                    ref targetBytesWritten);

                try
                {
                    // Ensure all generated bytes reach storage before verification.
                    tempStream.Flush(flushToDisk: true);
                }
                catch (IOException ex)
                {
                    throw new ConversionStageException(
                        ConversionErrorCode.TargetWriteError,
                        $"Failed to flush the temporary output file: {ex.Message}",
                        ex);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Recheck the source before installation to catch changes during conversion.
            FileInfo recheckInfo = new(sourcePath);

            if (IsReparsePoint(recheckInfo))
            {
                return Failure(
                    ConversionErrorCode.ReparsePointRejected,
                    "The source became a symbolic link or other reparse point during " +
                    "conversion; installation was rejected.",
                    sourceEncoding,
                    targetEncoding) with
                {
                    SourceBytes = sourceBytesProcessed,
                    TargetBytes = targetBytesWritten,
                };
            }

            if (recheckInfo.Length != capturedLength ||
                recheckInfo.LastWriteTimeUtc != capturedLastWriteUtc)
            {
                return Failure(
                    ConversionErrorCode.SourceChangedDuringConversion,
                    "The source file changed while it was being converted.",
                    sourceEncoding,
                    targetEncoding) with
                {
                    SourceBytes = sourceBytesProcessed,
                    TargetBytes = targetBytesWritten,
                };
            }

            // Verify length, BOM, and decoded content before installation.
            VerificationOutcome verification = VerifyTarget(
                sourceDigest,
                tempPath,
                targetEncoding,
                options.WriteBom,
                targetBytesWritten,
                effectiveBufferSize,
                cancellationToken);

            if (!verification.Success)
            {
                return new ConversionResult
                {
                    Success = false,
                    ErrorCode = verification.ErrorCode,
                    ErrorMessage = verification.Message,
                    SourceEncoding = sourceEncoding,
                    TargetEncoding = targetEncoding,
                    SourceBytes = sourceBytesProcessed,
                    TargetBytes = targetBytesWritten,
                    UnicodeScalarsVerified = verification.ScalarsCompared,
                    VerificationPassed = false,
                    BomVerificationPassed = verification.BomVerified,
                    ReplacementCommitted = false,
                };
            }

            // Record recovery metadata before the original can be replaced.
            string sourceFileSha = string.Empty;

            if (options.ExpectedSourceSha256 is not null ||
                options.RecordConversion is not null)
            {
                try
                {
                    sourceFileSha = ComputeFileSha256(sourcePath);
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException)
                {
                    sourceFileSha = string.Empty;
                }
            }

            // An approved plan must still refer to the bytes about to be replaced.
            if (options.ExpectedSourceSha256 is not null &&
                !string.Equals(
                    sourceFileSha,
                    options.ExpectedSourceSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Failure(
                    ConversionErrorCode.SourceChangedDuringConversion,
                    sourceFileSha.Length == 0
                        ? "The source file could not be re-read to confirm it still " +
                          "matches the one this conversion was approved for."
                        : "The source file no longer matches the one this conversion " +
                          "was approved for; it changed before installation.",
                    sourceEncoding,
                    targetEncoding) with
                {
                    SourceBytes = sourceBytesProcessed,
                    TargetBytes = targetBytesWritten,
                };
            }

            if (options.RecordConversion is not null)
            {
                string outputFileSha = RunIoStage(
                    ConversionErrorCode.TargetReadError,
                    "The verified output could not be hashed",
                    () => ComputeFileSha256(tempPath!));

                string? recordError = options.RecordConversion(new ConversionRecord
                {
                    SourcePath = sourcePath,
                    SourceBytes = sourceBytesProcessed,
                    SourceSha256 = sourceFileSha,
                    SourceTextSha256 = System.Convert.ToHexStringLower(sourceDigest.Hash),
                    OutputTextSha256 = System.Convert.ToHexStringLower(sourceDigest.Hash),
                    OutputSha256 = outputFileSha,
                    SourceEncoding = sourceEncoding.WebName,
                    SourceCodePage = sourceEncoding.CodePage,
                    SourceHasBom = sourceHadBom,
                    TargetEncoding = targetEncoding.WebName,
                    TargetCodePage = targetEncoding.CodePage,
                    TargetHasBom = options.WriteBom,
                    UnicodeScalars = verification.ScalarsCompared,
                });

                if (recordError is not null)
                {
                    return new ConversionResult
                    {
                        Success = false,
                        ErrorCode = ConversionErrorCode.RecoveryRecordError,
                        ErrorMessage =
                            "Conversion and verification succeeded, but the record needed to "
                            + $"reverse it could not be written: {recordError} The original "
                            + "was left unmodified rather than replaced with a conversion "
                            + "that could not be undone.",
                        SourceEncoding = sourceEncoding,
                        TargetEncoding = targetEncoding,
                        SourceBytes = sourceBytesProcessed,
                        TargetBytes = targetBytesWritten,
                        UnicodeScalarsVerified = verification.ScalarsCompared,
                        VerificationPassed = true,
                        BomVerificationPassed = true,
                        ReplacementCommitted = false,
                    };
                }
            }

            // Last cancellation point before installation.
            cancellationToken.ThrowIfCancellationRequested();

            // Apply preserved source metadata before the replacement.
            if (sameFile && (options.PreserveTimestamps || options.PreserveAttributes))
            {
                string? metadataError = RestoreTempFileMetadata(
                    tempPath,
                    options,
                    capturedAttributes,
                    capturedCreationTimeUtc,
                    capturedLastWriteUtc,
                    capturedLastAccessTimeUtc);

                if (metadataError is not null)
                {
                    return new ConversionResult
                    {
                        Success = false,
                        ErrorCode = ConversionErrorCode.MetadataRestoreFailed,
                        ErrorMessage = "Content conversion succeeded and content verification " +
                                       "succeeded, but required metadata could not be restored " +
                                       $"to the converted file before installation: {metadataError} " +
                                       "The original destination was left unmodified.",
                        SourceEncoding = sourceEncoding,
                        TargetEncoding = targetEncoding,
                        SourceBytes = sourceBytesProcessed,
                        TargetBytes = targetBytesWritten,
                        UnicodeScalarsVerified = verification.ScalarsCompared,
                        VerificationPassed = true,
                        BomVerificationPassed = true,
                        ReplacementCommitted = false,
                    };
                }
            }

            // Install only after all safety checks have passed.
            ConversionErrorCode? replaceError = AtomicReplace(
                tempPath,
                destinationPath,
                sameFile,
                out string? replaceErrorMessage,
                out bool metadataRestoreFailed,
                out bool? replacementCommitted);

            if (replaceError is not null)
            {
                return new ConversionResult
                {
                    Success = false,
                    ErrorCode = replaceError.Value,
                    ErrorMessage = replaceErrorMessage,
                    SourceEncoding = sourceEncoding,
                    TargetEncoding = targetEncoding,
                    SourceBytes = sourceBytesProcessed,
                    TargetBytes = targetBytesWritten,
                    UnicodeScalarsVerified = verification.ScalarsCompared,
                    VerificationPassed = true,
                    BomVerificationPassed = true,
                    ReplacementCommitted = replacementCommitted,
                };
            }

            tempPath = null;

            if (options.CompleteConversionRecord is not null)
            {
                string? completionError = options.CompleteConversionRecord();

                if (completionError is not null)
                {
                    return new ConversionResult
                    {
                        Success = false,
                        ErrorCode = ConversionErrorCode.RecoveryRecordError,
                        ErrorMessage =
                            "The converted file was installed and verified, but its recovery "
                            + $"record could not be marked complete: {completionError} The "
                            + "prepared record still contains the hashes needed to inspect it.",
                        SourceEncoding = sourceEncoding,
                        TargetEncoding = targetEncoding,
                        SourceBytes = sourceBytesProcessed,
                        TargetBytes = targetBytesWritten,
                        UnicodeScalarsVerified = verification.ScalarsCompared,
                        VerificationPassed = true,
                        BomVerificationPassed = true,
                        ReplacementCommitted = true,
                    };
                }
            }

            return new ConversionResult
            {
                Success = !metadataRestoreFailed,
                ErrorCode = metadataRestoreFailed
                    ? ConversionErrorCode.MetadataRestoreFailed
                    : ConversionErrorCode.None,
                ErrorMessage = metadataRestoreFailed
                    ? "Content conversion succeeded, content verification succeeded, and file " +
                      "replacement succeeded, but restoring the original destination file's " +
                      "own attributes failed."
                    : null,
                SourceEncoding = sourceEncoding,
                TargetEncoding = targetEncoding,
                SourceBytes = sourceBytesProcessed,
                TargetBytes = targetBytesWritten,
                UnicodeScalarsVerified = verification.ScalarsCompared,
                VerificationPassed = true,
                BomVerificationPassed = true,
                ReplacementCommitted = true,
            };
        }
        catch (OperationCanceledException)
        {
            return Failure(
                ConversionErrorCode.Cancelled,
                "The conversion was cancelled.",
                sourceEncoding,
                targetEncoding,
                sourceBytes: sourceBytesProcessed,
                targetBytes: targetBytesWritten);
        }
        catch (ConversionStageException ex)
        {
            return Failure(
                ex.ErrorCode,
                ex.Message,
                sourceEncoding,
                targetEncoding,
                ex.InnerException,
                sourceBytesProcessed,
                targetBytesWritten);
        }
        catch (DecoderFallbackException ex)
        {
            return Failure(
                ConversionErrorCode.SourceDecodeError,
                $"The source file could not be decoded using " +
                $"{sourceEncoding.EncodingName}: {DescribeDecoderFailure(ex)}",
                sourceEncoding,
                targetEncoding,
                ex,
                sourceBytesProcessed,
                targetBytesWritten);
        }
        catch (EncoderFallbackException ex)
        {
            return Failure(
                ConversionErrorCode.TargetEncodeError,
                $"Character {DescribeEncoderFailure(ex)} cannot be represented " +
                $"by {targetEncoding.EncodingName}.",
                sourceEncoding,
                targetEncoding,
                ex,
                sourceBytesProcessed,
                targetBytesWritten);
        }
        catch (NotSupportedException ex) when (IsMissingCodePageProvider(ex))
        {
            return Failure(
                ConversionErrorCode.CodePageProviderNotRegistered,
                "The requested target encoding requires a code-page provider " +
                "that has not been registered. Call " +
                "Encoding.RegisterProvider(CodePagesEncodingProvider.Instance) " +
                "once during application startup.",
                sourceEncoding,
                targetEncoding,
                ex,
                sourceBytesProcessed,
                targetBytesWritten);
        }
        catch (NotSupportedException ex)
        {
            return Failure(
                ConversionErrorCode.SourceDecodeError,
                "A strict decoder or encoder could not be constructed for this "
                + "conversion, so the original file was left unchanged: " + ex.Message,
                sourceEncoding,
                targetEncoding,
                ex,
                sourceBytesProcessed,
                targetBytesWritten);
        }
        catch (Exception ex)
        {
            return Failure(
                ConversionErrorCode.Unexpected,
                ex.Message,
                sourceEncoding,
                targetEncoding,
                ex,
                sourceBytesProcessed,
                targetBytesWritten);
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    #endregion

    #region Strict Encoding Configuration

    /// <summary>Creates a decoder with strict fallback.</summary>
    private static Decoder MakeStrictDecoder(Encoding encoding)
    {
        return TextEncoding.Strict(encoding).GetDecoder();
    }

    /// <summary>Creates an encoder with strict fallback.</summary>
    private static Encoder MakeStrictEncoder(Encoding encoding)
    {
        return TextEncoding.Strict(encoding).GetEncoder();
    }

    #endregion

    #region Conversion Pipeline

    /// <summary>
    /// Converts the source stream with persistent decoder/encoder state and hashes decoded content.
    /// </summary>
    private static ContentDigest StreamConvert(
        FileStream sourceStream,
        FileStream targetStream,
        Encoding sourceEncoding,
        Decoder sourceDecoder,
        Encoding targetEncoding,
        Encoder targetEncoder,
        int bufferSize,
        long totalSourceBytes,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken,
        ref long sourceBytesProcessed,
        ref long targetBytesWritten)
    {
        byte[] readBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        char[] charBuffer =
            ArrayPool<char>.Shared.Rent(sourceEncoding.GetMaxCharCount(bufferSize));
        byte[] writeBuffer =
            ArrayPool<byte>.Shared.Rent(targetEncoding.GetMaxByteCount(charBuffer.Length));

        try
        {
            using IncrementalHash sourceHash =
                IncrementalHash.CreateHash(ContentDigestAlgorithm);

            long scalarCount = 0;
            long codeUnitCount = 0;
            long lastProgressReportTicks = Environment.TickCount64;
            bool endOfStream;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                int bytesRead;

                try
                {
                    bytesRead = sourceStream.Read(readBuffer, 0, bufferSize);
                }
                catch (IOException ex)
                {
                    throw new ConversionStageException(
                        ConversionErrorCode.SourceReadError,
                        $"Failed to read the source file: {ex.Message}",
                        ex);
                }

                endOfStream = bytesRead == 0;
                sourceBytesProcessed += bytesRead;

                // Flush at EOF so incomplete trailing sequences are rejected.
                int charsWritten = sourceDecoder.GetChars(
                    readBuffer,
                    0,
                    bytesRead,
                    charBuffer,
                    0,
                    flush: endOfStream);

                if (charsWritten > 0)
                {
                    ReadOnlySpan<char> chars = charBuffer.AsSpan(0, charsWritten);

                    sourceHash.AppendData(MemoryMarshal.AsBytes(chars));
                    codeUnitCount += charsWritten;

                    foreach (Rune _ in chars.EnumerateRunes())
                    {
                        scalarCount++;
                    }
                }

                int bytesWritten = targetEncoder.GetBytes(
                    charBuffer,
                    0,
                    charsWritten,
                    writeBuffer,
                    0,
                    flush: endOfStream);

                if (bytesWritten > 0)
                {
                    try
                    {
                        targetStream.Write(writeBuffer, 0, bytesWritten);
                    }
                    catch (IOException ex)
                    {
                        throw new ConversionStageException(
                            ConversionErrorCode.TargetWriteError,
                            $"Failed to write the target file: {ex.Message}",
                            ex);
                    }

                    targetBytesWritten += bytesWritten;
                }

                // Progress is throttled, but cancellation remains immediate.
                if (progress is not null)
                {
                    long nowTicks = Environment.TickCount64;

                    if (endOfStream ||
                        nowTicks - lastProgressReportTicks >= ProgressReportIntervalMs)
                    {
                        lastProgressReportTicks = nowTicks;

                        try
                        {
                            progress.Report(new ConversionProgress
                            {
                                BytesProcessed = sourceBytesProcessed,
                                TotalBytes = totalSourceBytes,
                                Percentage = totalSourceBytes > 0
                                    ? Math.Min(
                                        100.0,
                                        (double)sourceBytesProcessed / totalSourceBytes * 100.0)
                                    : 100.0,
                            });
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch
                        {
                            // Progress reporting must not affect conversion.
                        }
                    }
                }
            }
            while (!endOfStream);

            return new ContentDigest(
                sourceHash.GetHashAndReset(),
                scalarCount,
                codeUnitCount);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
            ArrayPool<char>.Shared.Return(charBuffer);
            ArrayPool<byte>.Shared.Return(writeBuffer);
        }
    }

    #endregion

    #region Error Classification

    // Carries the error code for a specific conversion stage.
    private sealed class ConversionStageException : IOException
    {
        internal ConversionErrorCode ErrorCode { get; }

        internal ConversionStageException(
            ConversionErrorCode errorCode,
            string message,
            Exception inner)
            : base(message, inner)
        {
            ErrorCode = errorCode;
        }
    }

    /// <summary>Runs an I/O operation with a stage-specific error code.</summary>
    private static T RunIoStage<T>(
        ConversionErrorCode errorCode,
        string stageDescription,
        Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            throw new ConversionStageException(
                errorCode,
                $"{stageDescription}: {ex.Message}",
                ex);
        }
    }

    #endregion

    #region Result Helpers

    private static ConversionResult Failure(
        ConversionErrorCode errorCode,
        string message,
        Encoding sourceEncoding,
        Encoding targetEncoding,
        Exception? exception = null,
        long sourceBytes = 0,
        long targetBytes = 0)
    {
        return new ConversionResult
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = message,
            Exception = exception,
            SourceEncoding = sourceEncoding,
            TargetEncoding = targetEncoding,
            SourceBytes = sourceBytes,
            TargetBytes = targetBytes,
            ReplacementCommitted = false,
        };
    }

    private static string DescribeDecoderFailure(DecoderFallbackException ex)
    {
        // The index is relative to the failing read chunk.
        return
            $"invalid byte sequence (offset {ex.Index} within the failing read chunk).";
    }

    private static string DescribeEncoderFailure(EncoderFallbackException ex)
    {
        if (ex.IsUnknownSurrogate())
        {
            int codePoint =
                char.ConvertToUtf32(ex.CharUnknownHigh, ex.CharUnknownLow);

            return $"U+{codePoint:X4}";
        }

        return $"U+{(int)ex.CharUnknown:X4}";
    }

    private static bool IsMissingCodePageProvider(NotSupportedException ex)
    {
        // The current BCL exposes this condition through the exception message.
        return ex.Message.Contains(
            "RegisterProvider",
            StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
