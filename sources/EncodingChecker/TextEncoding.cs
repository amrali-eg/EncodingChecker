using System;
using System.Collections.Generic;
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
                @"The stream must be seekable.",
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
    /// Charset names EC knows how to ask the current runtime for.
    /// </summary>
    private static readonly string[] CharsetNames =
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
    /// Encodings from <see cref="CharsetNames"/> that the current .NET runtime can
    /// actually construct, with aliases reduced to one canonical code-page identity.
    /// </summary>
    /// <remarks>
    /// Both GUI encoding pickers use this resolved list. This prevents either picker
    /// from offering a name that conversion would subsequently reject as unavailable.
    /// </remarks>
    internal static IReadOnlyList<Encoding> SupportedEncodings { get; } =
        ResolveSupportedEncodings();


    private static IReadOnlyList<Encoding> ResolveSupportedEncodings()
    {
        var encodings = new List<Encoding>();
        var codePages = new HashSet<int>();

        foreach (string name in CharsetNames)
        {
            try
            {
                Encoding encoding = Encoding.GetEncoding(name);

                if (codePages.Add(encoding.CodePage))
                    encodings.Add(encoding);
            }
            catch (Exception ex) when (
                ex is ArgumentException or NotSupportedException)
            {
                // This runtime does not provide the named encoding.
            }
        }

        return encodings.AsReadOnly();
    }


    /// <summary>
    /// Returns an encoding whose decoder and encoder enforce strict fallback.
    /// </summary>
    /// <remarks>
    /// Fallbacks are supplied when the encoding is created because changing them
    /// afterwards is not reliable for code-page encodings.
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
            // Returning the original encoding here could silently re-enable its
            // replacement fallback. A caller that cannot obtain strict semantics must
            // refuse conversion instead.
            throw new NotSupportedException(
                $"Could not construct a strict codec for code page {encoding.CodePage}.",
                ex);
        }
    }

    /// <summary>
    /// Whether a detected source is safe to convert without asking the user to name the
    /// source codec. This deliberately covers only Unicode and ASCII.
    /// </summary>
    internal static bool IsUnicodeOrAscii(Encoding encoding) => encoding.CodePage is
        20127 or // US-ASCII
        65001 or // UTF-8
        1200 or  // UTF-16LE
        1201 or  // UTF-16BE
        12000 or // UTF-32LE
        12001;  // UTF-32BE


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
