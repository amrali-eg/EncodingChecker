using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace EncodingChecker;

internal static partial class EncodingConverter
{
    #region Target Verification

    private sealed record VerificationOutcome
    {
        internal bool Success { get; init; }
        internal ConversionErrorCode ErrorCode { get; init; }
        internal string? Message { get; init; }
        internal long ScalarsCompared { get; init; }

        /// <summary>Whether the target BOM state matched the requested policy.</summary>
        internal bool BomVerified { get; init; }
    }

    // Used only for same-run content verification.
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
        bool bomVerified = false;

        try
        {
            using FileStream targetStream = RunIoStage(
                ConversionErrorCode.TargetReadError,
                "Failed to reopen the temporary output file for verification",
                () => OpenReadShared(tempPath, bufferSize));

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
    /// SHA-256 over a file's raw bytes.
    /// </summary>
    private static string ComputeFileSha256(string path)
    {
        using FileStream stream = OpenReadShared(path, DefaultBufferSize);
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
}
