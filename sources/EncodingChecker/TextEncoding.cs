using System;
using System.Text;
using System.Buffers;
using System.IO;
using UtfUnknown;

namespace EncodingChecker;

/// <summary>
/// Orchestrates character-encoding detection for files, streams, and byte buffers.
///
/// Controls the detection sample size and delegates encoding detection
/// to specialized byte-level detectors. Unicode encodings are detected
/// using <see cref="UnicodeDetector"/>; if no Unicode encoding is detected,
/// UtfUnknown is used to obtain a legacy encoding candidate, which is then
/// independently verified using strict decoding and text validation before
/// being accepted.
/// </summary>
internal static class TextEncoding
{
    //
    // Maximum number of bytes sampled for encoding detection.
    //
    // 64 KiB is sufficient for the encoding detectors while limiting
    // unnecessary I/O for large files.
    //
    private const int DefaultMaxSampleBytes = 64 * 1024;

    //
    // Minimum sample size for reliably using entropy to reject binary data.
    //
    private const int MinimumEntropyProbeBytes = 512;

    //
    // Entropy threshold above which sufficiently large samples are treated
    // as likely binary, compressed, or encrypted data rather than text.
    //
    private const double BinaryEntropyThreshold = 7.4;


    /// <summary>
    /// Detects the character encoding of the specified file.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="maxSampleBytes">
    /// Maximum number of bytes to examine.
    /// </param>
    /// <returns>
    /// The detected <see cref="Encoding"/>, or <see langword="null"/> if
    /// the encoding could not be detected.
    /// </returns>
    internal static Encoding? DetectFromFile(
        string filePath,
        int maxSampleBytes = DefaultMaxSampleBytes)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxSampleBytes);

        using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.SequentialScan);

        return DetectFromStream(stream, maxSampleBytes);
    }


    /// <summary>
    /// Detects the character encoding by reading a sample from the
    /// specified stream.
    /// </summary>
    /// <remarks>
    /// The stream must be seekable. The original stream position is
    /// restored when the method returns.
    /// </remarks>
    /// <param name="stream">
    /// Seekable stream containing the data to examine.
    /// </param>
    /// <param name="maxSampleBytes">
    /// Maximum number of bytes to examine.
    /// </param>
    /// <returns>
    /// The detected <see cref="Encoding"/>, or <see langword="null"/> if
    /// the encoding could not be detected.
    /// </returns>
    internal static Encoding? DetectFromStream(
        Stream stream,
        int maxSampleBytes = DefaultMaxSampleBytes)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            throw new ArgumentException(
                "The stream must be seekable.",
                nameof(stream));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxSampleBytes);

        if (stream.Length == 0L)
            return null;

        long originalPosition = stream.Position;

        int bytesToRead = (int)Math.Min(
            stream.Length,
            maxSampleBytes);

        byte[] buffer =
            ArrayPool<byte>.Shared.Rent(bytesToRead);

        try
        {
            //
            // Always inspect the stream from the beginning.
            //
            stream.Position = 0;

            int bytesRead = stream.ReadAtLeast(
                buffer.AsSpan(0, bytesToRead),
                bytesToRead,
                throwOnEndOfStream: false);

            if (bytesRead == 0)
                return null;

            return DetectFromBuffer(
                buffer.AsSpan(0, bytesRead),
                maxSampleBytes);
        }
        finally
        {
            stream.Position = originalPosition;
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }


    /// <summary>
    /// Detects the character encoding of the specified byte buffer.
    /// </summary>
    /// <param name="buffer">
    /// Buffer containing the bytes to examine.
    /// </param>
    /// <param name="maxSampleBytes">
    /// Maximum number of bytes to examine for encoding detection.
    /// </param>
    /// <returns>
    /// The detected <see cref="Encoding"/>, or <see langword="null"/> if
    /// the encoding could not be detected.
    /// </returns>
    internal static Encoding? DetectFromBuffer(
        ReadOnlySpan<byte> buffer,
        int maxSampleBytes = DefaultMaxSampleBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxSampleBytes);

        int bytesToExamine = Math.Min(
            buffer.Length,
            maxSampleBytes);

        if (bytesToExamine == 0)
            return null;

        buffer = buffer[..bytesToExamine];

        // Reject high-entropy data as likely binary.
        if (buffer.Length >= MinimumEntropyProbeBytes &&
            BinaryEntropy(buffer) > BinaryEntropyThreshold)
        {
            return null;
        }

        // 1. Detect Unicode.
        Encoding? encoding =
            UnicodeDetector.DetectFromBuffer(buffer);

        if (encoding != null)
            return encoding;

        // 2. Detect a legacy encoding using UtfUnknown.
        //
        // https://github.com/CharsetDetector/UTF-unknown
        //
        byte[] bytes = [.. buffer];

        DetectionResult? result;

        try
        {
            result = CharsetDetector.DetectFromBytes(bytes);
        }
        catch (Exception)
        {
            // UtfUnknown's exception surface for malformed input isn't documented; treat
            // any failure the same as "no legacy encoding detected" rather than letting it
            // abort the caller's per-file processing.
            result = null;
        }

        DetectionDetail? detected =
            result?.Detected;

        // Get the System.Text.Encoding of the found encoding (can be null if not available)
        Encoding? legacyEncoding =
            detected?.Encoding;

        if (legacyEncoding is null)
            return null;

        // 3. Independently validate UtfUnknown's result.
        return TextValidation.IsValidText(
            legacyEncoding,
            buffer)
            ? legacyEncoding
            : null;
    }


    #region Helpers

    /// <summary>
    /// Computes Shannon entropy (bits per byte) over the detector sample.
    /// </summary>
    private static double BinaryEntropy(
        ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
            return 0.0;

        Span<int> histogram = stackalloc int[256];
        foreach (byte b in buffer)
        {
            histogram[b]++;
        }
        double entropy = 0.0;
        foreach (int frequency in histogram)
        {
            if (frequency == 0)
                continue;
            double probability =
                (double)frequency / buffer.Length;
            entropy -=
                probability * Math.Log2(probability);
        }
        return entropy;
    }

    #endregion
}
