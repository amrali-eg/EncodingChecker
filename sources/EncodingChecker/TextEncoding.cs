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

        // Every detection route funnels through here, so this is the one place that has
        // to record it. See DetectionCounters for why the count exists.
        DetectionCounters.RecordDetection();

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
    /// Every charset EC can name or convert to.
    /// </summary>
    /// <remarks>
    /// Shared with <see cref="EncodingAmbiguity"/>, which needs the same set to decide
    /// whether a file's bytes identify one encoding or several. That question must be
    /// answered against what EC actually supports; deriving the candidates from any
    /// other source would make the answer depend on something the user cannot see.
    /// </remarks>
    internal static readonly string[] SupportedCharsets =
    [
        "ascii", "utf-8", "utf-16le", "utf-16be",
        "utf-32le", "utf-32be",
        "euc-jp", "euc-kr", "euc-tw",
        "iso-2022-cn", "iso-2022-kr", "iso-2022-jp",
        "x-cp50227",
        "big5", "gb18030", "hz-gb-2312", "shift-jis",
        "ks_c_5601-1987", "cp949",
        "ibm852", "ibm855", "ibm866",
        "iso-8859-1", "iso-8859-2", "iso-8859-3",
        "iso-8859-4", "iso-8859-5", "iso-8859-6",
        "iso-8859-7", "iso-8859-8", "iso-8859-9",
        "iso-8859-10", "iso-8859-11", "iso-8859-13",
        "iso-8859-15", "iso-8859-16",
        "windows-1250", "windows-1251", "windows-1252",
        "windows-1253", "windows-1255", "windows-1256",
        "windows-1257", "windows-1258",
        "x-mac-ce", "x-mac-cyrillic",
        "koi8-r", "tis-620", "viscii",
        "X-ISO-10646-UCS-4-3412",
        "X-ISO-10646-UCS-4-2143"
    ];



    /// <summary>
    /// Returns an encoding whose decoder and encoder actually enforce strict fallback.
    /// </summary>
    /// <remarks>
    /// Assigning <see cref="Decoder.Fallback"/> or <see cref="Encoder.Fallback"/> after
    /// <see cref="Encoding.GetDecoder"/>/<see cref="Encoding.GetEncoder"/> has no effect for
    /// the encodings supplied by <see cref="System.Text.CodePagesEncodingProvider"/>: those
    /// codecs take their fallbacks from the parent <see cref="Encoding"/> when they are
    /// created, so a later assignment is silently ignored and unmappable input is replaced
    /// instead of throwing. The fallbacks must be supplied up front, to
    /// <see cref="Encoding.GetEncoding(int, EncoderFallback, DecoderFallback)"/>.
    /// <para>
    /// Only the codecs come from the returned encoding; callers that emit a preamble must
    /// keep using their original instance, which is what carries the requested BOM policy.
    /// </para>
    /// </remarks>
    internal static Encoding Strict(
        Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        if (encoding.DecoderFallback is DecoderExceptionFallback &&
            encoding.EncoderFallback is EncoderExceptionFallback)
        {
            return encoding;
        }

        try
        {
            return Encoding.GetEncoding(
                encoding.CodePage,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // An encoding that cannot be rebuilt from its code page keeps the original
            // instance rather than failing the caller outright.
            return encoding;
        }
    }


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
