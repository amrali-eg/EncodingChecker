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
    TemporaryFileError,
    ReplacementError,
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
    internal int BufferSize { get; init; } = EncodingConverter.DEFAULT_BUFFER_SIZE;

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
    /// Invoked after verification succeeds and before the converted file is installed,
    /// to record how this conversion can be undone. Returns <see langword="null"/> on
    /// success, or a message describing why the record could not be written.
    /// </summary>
    /// <remarks>
    /// Deliberately inside the safety boundary rather than after it. Recovery information
    /// written once the original has already been replaced is recovery information that
    /// might not exist when it is needed, which is the gap it exists to close: a
    /// conversion is only reversible for someone who still knows which codec produced it.
    /// A failure here therefore aborts the conversion with the original intact.
    /// </remarks>
    internal Func<ConversionRecord, string?>? RecordConversion { get; init; }

    /// <summary>
    /// The SHA-256 the source is required to still have at the moment of installation,
    /// or <see langword="null"/> to skip the check.
    /// </summary>
    /// <remarks>
    /// Set when the decision to convert this file was made earlier, against bytes that
    /// were read then - which is what a conversion plan is. The length-and-timestamp
    /// recheck above catches a source rewritten during conversion, but it compares
    /// against what this run itself observed on opening the file, so it cannot speak to
    /// anything that happened before that. A plan can.
    /// <para>
    /// This narrows the window rather than closing it: a source rewritten between this
    /// check and <c>File.Replace</c> is still not detected. Eliminating that entirely
    /// needs the source held open against writers for the whole conversion, which would
    /// fail conversions of files legitimately open elsewhere.
    /// </para>
    /// </remarks>
    internal string? ExpectedSourceSha256 { get; init; }
}

/// <summary>
/// Everything needed to reverse or independently reconstruct one conversion, without
/// reference to any external report.
/// </summary>
internal sealed record ConversionRecord
{
    internal required string SourcePath { get; init; }

    internal required long SourceBytes { get; init; }

    /// <summary>SHA-256 of the source file's bytes, before conversion.</summary>
    internal required string SourceSha256 { get; init; }

    /// <summary>SHA-256 over the decoded source text, independent of encoding.</summary>
    internal required string SourceTextSha256 { get; init; }

    /// <summary>SHA-256 over the decoded converted text. Equal to the source text hash
    /// on a verified conversion; recorded so that equality is checkable later.</summary>
    internal required string OutputTextSha256 { get; init; }

    internal required string SourceEncoding { get; init; }

    /// <summary>
    /// The code page, which identifies the codec unambiguously where a name may not:
    /// "cp949" and "ks_c_5601-1987" name one encoding, and only the number says so.
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
    /// Whether replacement is known to have occurred.
    /// <see langword="false"/> means it did not occur;
    /// <see langword="true"/> means it occurred;
    /// <see langword="null"/> means the outcome is unknown.
    /// </summary>
    internal bool? ReplacementCommitted { get; init; }
}

#endregion

/// <summary>
/// Converts text files between encodings without data loss.
/// Verifies converted content before replacement.
/// </summary>
internal static class EncodingConverter
{
    #region Constants

    // Default: 256 KiB, capped to the source file size.
    internal const int DEFAULT_BUFFER_SIZE = 256 * 1024;

    // Minimum interval between progress reports.
    private const long PROGRESS_REPORT_INTERVAL_MS = 100;

    // Temporary file suffix. Internal so ScanEngine can exclude these from scans too.
    internal const string TEMP_FILE_SUFFIX = "unicodechecker.tmp";

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

        // Reject impossible BOM requests before I/O.
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

        // Keep progress counters available to error handlers.
        long sourceBytesProcessed = 0;
        long targetBytesWritten = 0;

        // Whether the source carried a BOM, captured for the conversion record,
        // which is written after the stream that observed it has closed.
        bool sourceHadBom = false;

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

            // Open first so metadata describes the file held by this handle.
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

                // Limit the buffer to the source size while keeping it at least 1.
                effectiveBufferSize =
                    (int)Math.Max(1, Math.Min(options.BufferSize, capturedLength));

                tempPath = CreateTempFilePath(destinationPath);

                string path = tempPath;
                using FileStream tempStream = RunIoStage(
                    ConversionErrorCode.TemporaryFileError,
                    "Failed to create the temporary output file",
                    () => CreateTempStream(path, effectiveBufferSize));

                // Consume a compatible source BOM; its absence is allowed.
                int sourcePreambleLength = RunIoStage(
                    ConversionErrorCode.SourceReadError,
                    "Failed to read the source file's leading bytes",
                    () => ConsumePreambleIfPresent(sourceStream, sourceEncoding));

                sourceBytesProcessed += sourcePreambleLength;
                sourceHadBom = sourcePreambleLength > 0;

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
                    // Flush buffered data to storage before verification.
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

            // Point-in-time check only, not a full TOCTOU guarantee - the source
            // could still change between here and the install below.
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

            // Verify length, BOM, and decoded content.
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

            // Record how to undo this, before anything is overwritten. Ordered here on
            // purpose: the backup already exists, the conversion is verified, and the
            // original is still in place, so a failure to record leaves nothing to
            // recover from and nothing needing recovery.
            // Hashed only when something asks for it, so the extra read is not paid for
            // by conversions that need neither the check nor the record.
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

            // The last point at which nothing has been installed. A caller that decided
            // to convert these bytes earlier gets to insist they are still those bytes.
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
                string? recordError = options.RecordConversion(new ConversionRecord
                {
                    SourcePath = sourcePath,
                    SourceBytes = sourceBytesProcessed,
                    SourceSha256 = sourceFileSha,
                    SourceTextSha256 = System.Convert.ToHexStringLower(sourceDigest.Hash),
                    OutputTextSha256 = System.Convert.ToHexStringLower(sourceDigest.Hash),
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
                        ErrorCode = ConversionErrorCode.TargetWriteError,
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

            // Final cancellation checkpoint before installation.
            cancellationToken.ThrowIfCancellationRequested();

            // Restore source metadata on the temp file before installation.
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

            // Install only after verification and metadata restoration succeed.
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

            return new ConversionResult
            {
                // Replacement succeeded; only destination metadata restoration can fail here.
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
            // Preserve the error code for the failed I/O stage.
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
        catch (Exception ex)
        {
            // Preserve unexpected exceptions for diagnostics.
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
            // Remove the temp file unless it was installed.
            TryDeleteTempFile(tempPath);
        }
    }

    #endregion

    #region Strict Encoding Configuration

    /// <summary>Creates a decoder with strict fallback.</summary>
    /// <remarks>
    /// <see cref="TextEncoding.Strict"/> is what makes the fallback stick: assigning
    /// <see cref="Decoder.Fallback"/> on its own is silently ignored by the
    /// <see cref="CodePagesEncodingProvider"/> encodings, which would let invalid bytes be
    /// substituted instead of raising. The BOM still comes from the caller's original
    /// encoding instance, so preamble handling is unaffected.
    /// </remarks>
    private static Decoder MakeStrictDecoder(Encoding encoding)
    {
        Decoder decoder = TextEncoding.Strict(encoding).GetDecoder();
        decoder.Fallback = DecoderFallback.ExceptionFallback;
        return decoder;
    }

    /// <summary>Creates an encoder with strict fallback.</summary>
    /// <remarks>See <see cref="MakeStrictDecoder"/>; the same applies to the encoder.</remarks>
    private static Encoder MakeStrictEncoder(Encoding encoding)
    {
        Encoder encoder = TextEncoding.Strict(encoding).GetEncoder();
        encoder.Fallback = EncoderFallback.ExceptionFallback;
        return encoder;
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

                // Flush only at EOF to detect incomplete trailing sequences.
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

                // Throttle progress reports; cancellation remains immediate.
                if (progress is not null)
                {
                    long nowTicks = Environment.TickCount64;

                    if (endOfStream ||
                        nowTicks - lastProgressReportTicks >= PROGRESS_REPORT_INTERVAL_MS)
                    {
                        lastProgressReportTicks = nowTicks;

                        // Ignore callback failures; allow cancellation through.
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
                            // Progress is observational.
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

    #region Target Verification

    private sealed record VerificationOutcome
    {
        internal bool Success { get; init; }
        internal ConversionErrorCode ErrorCode { get; init; }
        internal string? Message { get; init; }
        internal long ScalarsCompared { get; init; }

        /// <summary>
        /// Whether the target BOM state was confirmed to match the requested policy.
        /// </summary>
        internal bool BomVerified { get; init; }
    }

    // SHA-256 is used only for same-run content verification.
    private static readonly HashAlgorithmName ContentDigestAlgorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// Verifies target length, BOM, and decoded content without rereading the source.
    /// </summary>
    private static VerificationOutcome VerifyTarget(
        ContentDigest sourceDigest,
        string tempPath,
        Encoding targetEncoding,
        bool bomExpected,
        long expectedTargetBytes,
        int bufferSize,
        CancellationToken cancellationToken)
    {
        // Retained so decode failures can report whether BOM verification completed.
        bool bomVerified = false;

        try
        {
            using FileStream targetStream = RunIoStage(
                ConversionErrorCode.TargetReadError,
                "Failed to reopen the temporary output file for verification",
                () => OpenReadShared(tempPath, bufferSize));

            // Verify the expected byte count.
            if (targetStream.Length != expectedTargetBytes)
            {
                return new VerificationOutcome
                {
                    Success = false,
                    ErrorCode = ConversionErrorCode.VerificationFailed,
                    Message = $"The generated file is {targetStream.Length} byte(s), but " +
                              $"{expectedTargetBytes} byte(s) were written during conversion.",
                };
            }

            if (targetEncoding.GetPreamble().Length > 0)
            {
                bool actualBomPresent;

                try
                {
                    actualBomPresent =
                        ConsumePreambleIfPresent(targetStream, targetEncoding) > 0;
                }
                catch (IOException ex)
                {
                    throw new ConversionStageException(
                        ConversionErrorCode.TargetReadError,
                        $"Failed to read the target file's leading bytes: {ex.Message}",
                        ex);
                }

                if (actualBomPresent != bomExpected)
                {
                    return new VerificationOutcome
                    {
                        Success = false,
                        ErrorCode = ConversionErrorCode.BomMismatch,
                        Message = bomExpected
                            ? "The target file is missing the expected byte-order mark."
                            : "The target file unexpectedly contains a byte-order mark.",
                    };
                }
            }

            // BOM state is now confirmed.
            bomVerified = true;

            Decoder targetDecoder = MakeStrictDecoder(targetEncoding);

            ContentDigest targetDigest = ComputeContentDigest(
                targetStream,
                targetEncoding,
                targetDecoder,
                bufferSize,
                cancellationToken);

            bool contentMatches =
                sourceDigest.Hash.AsSpan().SequenceEqual(targetDigest.Hash);

            if (contentMatches)
            {
                return new VerificationOutcome
                {
                    Success = true,
                    ScalarsCompared = sourceDigest.ScalarCount,
                    BomVerified = bomVerified,
                };
            }

            string message =
                sourceDigest.Utf16CodeUnitCount != targetDigest.Utf16CodeUnitCount
                    ? $"Decoded content length differs from source: source decoded " +
                      $"to {sourceDigest.ScalarCount} Unicode scalar(s) " +
                      $"({sourceDigest.Utf16CodeUnitCount} UTF-16 code units), target " +
                      $"decoded to {targetDigest.ScalarCount} scalar(s) " +
                      $"({targetDigest.Utf16CodeUnitCount} UTF-16 code units)."
                    : $"Decoded content differs from source (both decode to " +
                      $"{sourceDigest.ScalarCount} Unicode scalar(s), but the " +
                      $"content is not identical).";

            return new VerificationOutcome
            {
                Success = false,
                ErrorCode = ConversionErrorCode.UnicodeMismatch,
                Message = message,
                ScalarsCompared =
                    Math.Min(sourceDigest.ScalarCount, targetDigest.ScalarCount),
                BomVerified = bomVerified,
            };
        }
        catch (DecoderFallbackException ex)
        {
            // Bytes were readable but invalid for the target encoding.
            return new VerificationOutcome
            {
                Success = false,
                ErrorCode = ConversionErrorCode.TargetDecodeError,
                Message = $"The generated file could not be re-decoded for " +
                          $"verification: {DescribeDecoderFailure(ex)}",
                BomVerified = bomVerified,
            };
        }
    }

    private readonly record struct ContentDigest(
        byte[] Hash,
        long ScalarCount,
        long Utf16CodeUnitCount);

    /// <summary>
    /// Hashes the exact decoded UTF-16 content and returns scalar/code-unit counts.
    /// No normalization is performed.
    /// </summary>
    /// <summary>SHA-256 over a file's raw bytes.</summary>
    private static string ComputeFileSha256(string path)
    {
        using FileStream stream = OpenReadShared(path, DEFAULT_BUFFER_SIZE);
        using var sha = SHA256.Create();
        return System.Convert.ToHexStringLower(sha.ComputeHash(stream));
    }

    private static ContentDigest ComputeContentDigest(
        Stream stream,
        Encoding encoding,
        Decoder decoder,
        int bufferSize,
        CancellationToken cancellationToken)
    {
        byte[] byteBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        char[] charBuffer =
            ArrayPool<char>.Shared.Rent(encoding.GetMaxCharCount(bufferSize));

        try
        {
            using IncrementalHash hash =
                IncrementalHash.CreateHash(ContentDigestAlgorithm);

            long scalarCount = 0;
            long codeUnitCount = 0;
            bool endOfStream;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                int bytesRead;

                try
                {
                    bytesRead = stream.Read(byteBuffer, 0, bufferSize);
                }
                catch (IOException ex)
                {
                    throw new ConversionStageException(
                        ConversionErrorCode.TargetReadError,
                        $"Failed to read the target file: {ex.Message}",
                        ex);
                }

                endOfStream = bytesRead == 0;

                int charsWritten = decoder.GetChars(
                    byteBuffer,
                    0,
                    bytesRead,
                    charBuffer,
                    0,
                    flush: endOfStream);

                if (charsWritten > 0)
                {
                    ReadOnlySpan<char> chars = charBuffer.AsSpan(0, charsWritten);

                    hash.AppendData(MemoryMarshal.AsBytes(chars));
                    codeUnitCount += charsWritten;

                    foreach (Rune _ in chars.EnumerateRunes())
                    {
                        scalarCount++;
                    }
                }
            }
            while (!endOfStream);

            return new ContentDigest(
                hash.GetHashAndReset(),
                scalarCount,
                codeUnitCount);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(byteBuffer);
            ArrayPool<char>.Shared.Return(charBuffer);
        }
    }

    #endregion

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
            $"{fileName}.{Guid.NewGuid():N}.{TEMP_FILE_SUFFIX}");
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
            // Cleanup failure does not affect the conversion result.
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
    /// Preserves only <see cref="FileAttributes"/> and the three standard timestamps.
    /// ACLs, alternate streams, compression, encryption, extended attributes, and other
    /// filesystem-specific metadata are not preserved.
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
    /// Also handles destination ReadOnly state.
    /// </summary>
    /// <param name="metadataRestoreFailed">Indicates whether metadata restoration failed.</param>
    /// <param name="replacementCommitted">
    /// True if replacement occurred, false if it did not, or null if the outcome is unknown.
    /// </param>
    /// <param name="destinationPath">The path to the destination file.</param>
    /// <param name="sameFile">Indicates whether the source and destination files are the same.</param>
    /// <param name="tempPath">The path to the temporary file.</param>
    /// <param name="errorMessage">A message describing the error, if the operation fails.</param>
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

            // Final reparse-point check before replacement.
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
                // A failed rename may have consumed the temp file; use its presence to classify
                // the outcome without incorrectly modifying the destination.
                bool tempStillExists = File.Exists(tempPath);

                string? rollbackError = null;

                if (tempStillExists)
                {
                    // Replacement did not consume the temp file; restore ReadOnly if changed.
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

            // Restore the destination's original ReadOnly state when the installed file
            // does not already carry it.
            //
            // For a different destination it never does, so this always applies, as
            // before. For a same-file replacement the temp normally arrives holding the
            // original attributes - but only because they were applied to it before
            // installation, which PreserveAttributes = false skips. Keying off the
            // installed file's actual state rather than sameFile alone stops ReadOnly
            // being dropped silently in that combination.
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
    /// <see cref="File.Replace(string, string, string?, bool)"/> when the destination
    /// exists (falling back to <see cref="File.Move(string, string, bool)"/> only if the
    /// platform doesn't support File.Replace), or a plain move when it doesn't.
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
                // Ignore metadata outside this converter's preservation contract.
                File.Replace(
                    tempPath,
                    destinationPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            catch (PlatformNotSupportedException)
            {
                // Fallback is not guaranteed to be atomic.
                File.Move(tempPath, destinationPath, overwrite: true);
            }
        }
        else
        {
            File.Move(tempPath, destinationPath);
        }
    }

    /// <summary>
    /// Atomically installs a backup file: replaces (or creates) <paramref name="destinationPath"/>
    /// with the already-written <paramref name="tempPath"/>, reusing the same
    /// <see cref="ReplaceOrMove"/> logic as the main conversion's install step, temporarily
    /// clearing and restoring a ReadOnly destination attribute if needed. Unlike
    /// <see cref="AtomicReplace"/>, failures propagate as ordinary exceptions rather than a
    /// <see cref="ConversionErrorCode"/> - backup writes don't need that richer contract, since
    /// the caller already has its own simple "backup failed" error path.
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
            // The replacement failure is the real error. Restoring ReadOnly from a plain
            // finally would let a failing SetAttributes throw over it, reporting an
            // attribute problem instead of why the backup actually failed - so the
            // rollback is attempted here and only ever added as context, matching
            // AtomicReplace's handling of the same situation.
            string? rollbackError = TryRestoreReadOnly(destinationPath, clearedAttributes);

            if (rollbackError is null)
                throw;

            throw new IOException(
                $"{replaceEx.Message} The original ReadOnly attribute could also not " +
                $"be restored: {rollbackError}",
                replaceEx);
        }

        // Replacement succeeded; a restoration failure here is itself the primary error.
        string? restoreError = TryRestoreReadOnly(destinationPath, clearedAttributes);

        if (restoreError is not null)
        {
            throw new IOException(
                $"The backup was written, but the original ReadOnly attribute could " +
                $"not be restored: {restoreError}");
        }
    }


    /// <summary>
    /// Restores previously cleared attributes, returning the failure message instead of
    /// throwing so a caller can decide whether it outranks an error already in flight.
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

    #region Error Classification

    // Carries the stage-specific conversion error code.
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

    /// <summary>Runs an I/O operation and assigns its stage-specific error code.</summary>
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
        // Index is relative to the failing read chunk.
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
        // Current BCL exposes this condition only through the exception message.
        return ex.Message.Contains(
            "RegisterProvider",
            StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
