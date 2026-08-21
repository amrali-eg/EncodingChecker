using System;
using System.Text;

namespace EncodingChecker;

/// <summary>
/// Detects Unicode text encodings from raw byte buffers.
///
/// Supported encodings:
/// - ASCII
/// - UTF-8 (with or without BOM)
/// - UTF-16 Little Endian (with or without BOM)
/// - UTF-16 Big Endian (with or without BOM)
/// - UTF-32 Little Endian (with or without BOM)
/// - UTF-32 Big Endian (with or without BOM)
///
/// BOM-less Unicode encodings are identified using encoding-specific
/// byte-level heuristics and confirmed using <see cref="TextValidation"/>.
/// </summary>
internal static class UnicodeDetector
{
    #region Cached Encodings

    // UTF-8
    internal static readonly UTF8Encoding Utf8Bom = new(true);

    internal static readonly UTF8Encoding Utf8NoBom = new(false);

    // UTF-16
    internal static readonly UnicodeEncoding Utf16LittleEndianBom =
        new(
            bigEndian: false,
            byteOrderMark: true);

    internal static readonly UnicodeEncoding Utf16LittleEndianNoBom =
        new(
            bigEndian: false,
            byteOrderMark: false);

    internal static readonly UnicodeEncoding Utf16BigEndianBom =
        new(
            bigEndian: true,
            byteOrderMark: true);

    internal static readonly UnicodeEncoding Utf16BigEndianNoBom =
        new(
            bigEndian: true,
            byteOrderMark: false);

    // UTF-32
    internal static readonly UTF32Encoding Utf32LittleEndianBom =
        new(
            bigEndian: false,
            byteOrderMark: true);

    internal static readonly UTF32Encoding Utf32LittleEndianNoBom =
        new(
            bigEndian: false,
            byteOrderMark: false);

    internal static readonly UTF32Encoding Utf32BigEndianBom =
        new(
            bigEndian: true,
            byteOrderMark: true);

    internal static readonly UTF32Encoding Utf32BigEndianNoBom =
        new(
            bigEndian: true,
            byteOrderMark: false);

    #endregion


    #region Byte Scanner

    //
    // UTF-32 detection needs at least four code units (4 bytes each)
    // for the candidate validation and text-quality confirmation.
    //
    private const int MinimumUtf32ProbeBytes = 16;


    //
    // UTF-16 detection requires a minimum sample for the endianness
    // heuristic in CheckUtf16 to provide useful evidence.
    //
    private const int MinimumUtf16ProbeBytes = 10;


    //
    // Minimum NUL-byte density typically observed in UTF-16 text.
    //
    private const double Utf16MinNullFraction = 0.02;


    //
    // Minimum channel χ² for the UTF-16 fallback heuristic.
    //
    private const int Utf16ChannelChiSquareThreshold = 300;


    /// <summary>
    /// Detects the Unicode encoding of the specified byte buffer.
    /// </summary>
    /// <param name="buffer">Buffer containing the bytes to examine.</param>
    /// <returns>
    /// An <see cref="Encoding"/> configured for the detected byte order
    /// (and BOM status) of the detected encoding; otherwise <see langword="null"/>.
    /// </returns>
    internal static Encoding? DetectFromBuffer(
        ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
            return null;

        //
        // Check for a Unicode BOM first.
        //
        Encoding? bomEncoding = CheckBom(buffer);

        //
        // No BOM: run the BOM-less detectors.
        //
        return bomEncoding ?? CheckBomless(buffer);
    }


    private static readonly byte[] Utf32LeBomBytes = [0xFF, 0xFE, 0x00, 0x00];
    private static readonly byte[] Utf32BeBomBytes = [0x00, 0x00, 0xFE, 0xFF];
    private static readonly byte[] Utf8BomBytes    = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LeBomBytes = [0xFF, 0xFE];
    private static readonly byte[] Utf16BeBomBytes = [0xFE, 0xFF];

    /// <summary>
    /// Detects a Unicode byte-order mark (BOM) at the beginning
    /// of the buffer.
    /// </summary>
    /// <returns>
    /// The corresponding encoding; otherwise <see langword="null"/>.
    /// </returns>
    private static Encoding? CheckBom(ReadOnlySpan<byte> buffer)
    {
        // Check longer BOMs first because UTF-32LE begins with
        // the same bytes as the UTF-16LE BOM (FF FE).

        // UTF-32LE
        if (buffer.StartsWith(Utf32LeBomBytes))
            return Utf32LittleEndianBom;

        // UTF-32BE
        if (buffer.StartsWith(Utf32BeBomBytes))
            return Utf32BigEndianBom;

        // UTF-8
        if (buffer.StartsWith(Utf8BomBytes))
            return Utf8Bom;

        // UTF-16LE
        if (buffer.StartsWith(Utf16LeBomBytes))
            return Utf16LittleEndianBom;

        // UTF-16BE
        if (buffer.StartsWith(Utf16BeBomBytes))
            return Utf16BigEndianBom;

        return null;
    }


    /// <summary>
    /// Runs BOM-less Unicode encoding detection in a fixed order:
    /// UTF-32, UTF-16, ASCII, then UTF-8.
    /// </summary>
    private static Encoding? CheckBomless(
        ReadOnlySpan<byte> buffer)
    {
        //
        // Detection order:
        //   1. UTF-32
        //   2. UTF-16
        //   3. ASCII
        //   4. UTF-8
        //
        // Unknown or unsupported encodings return null.

        //
        // UTF-32 without BOM.
        //
        Encoding? utf32 = CheckUtf32(buffer);

        if (utf32 != null)
            return utf32;

        //
        // UTF-16 without BOM.
        //
        Encoding? utf16 = CheckUtf16(buffer);

        if (utf16 != null)
            return utf16;

        //
        // ASCII-only data is also valid UTF-8, so classify it separately first.
        //
        if (IsAscii(buffer))
            return Encoding.ASCII;

        //
        // UTF-8 without BOM.
        //
        Encoding? utf8 = CheckUtf8(buffer);

        if (utf8 != null)
            return utf8;

        //
        // Unknown or unsupported encoding.
        //
        return null;
    }


    /// <summary>
    /// Detects BOM-less UTF-32 using a cheap positional-NUL prefilter,
    /// then confirms the candidate with strict decoding and text validation.
    /// </summary>
    private static UTF32Encoding? CheckUtf32(
        ReadOnlySpan<byte> buffer)
    {
        //
        // Ignore an incomplete final code unit.
        //
        int trimmedLength = buffer.Length & ~3;

        if (trimmedLength < MinimumUtf32ProbeBytes)
            return null;

        buffer = buffer[..trimmedLength];

        bool beCandidate = true;
        bool leCandidate = true;

        //
        // For valid Unicode scalar values (U+0000..U+10FFFF), the most
        // significant UTF-32 byte is always zero.
        //
        for (int i = 0; i < buffer.Length; i += 4)
        {
            if (beCandidate && buffer[i] != 0)
                beCandidate = false;

            if (leCandidate && buffer[i + 3] != 0)
                leCandidate = false;

            // Early rejection if neither byte order is plausible.
            if (!beCandidate && !leCandidate)
                return null;
        }

        //
        // Prefer UTF-32LE when both byte orders remain plausible.
        //
        if (leCandidate &&
            TextValidation.IsValidText(Utf32LittleEndianNoBom, buffer))
        {
            return Utf32LittleEndianNoBom;
        }

        if (beCandidate &&
            TextValidation.IsValidText(Utf32BigEndianNoBom, buffer))
        {
            return Utf32BigEndianNoBom;
        }

        return null;
    }


    /// <summary>
    /// Detects BOM-less UTF-16 using positional NUL density and, when necessary,
    /// byte-channel distribution as heuristic evidence, then confirms the selected
    /// byte order through strict UTF-16 decoding and text-quality validation.
    /// </summary>
    private static UnicodeEncoding? CheckUtf16(
        ReadOnlySpan<byte> buffer)
    {
        //
        // Ignore an incomplete final code unit.
        //
        int trimmedLength = buffer.Length & ~1;
        if (trimmedLength < MinimumUtf16ProbeBytes)
            return null;

        buffer = buffer[..trimmedLength];
        int numUnits = buffer.Length / 2;

        //
        // even-position bytes are the UTF-16BE high-byte channel;
        // odd-position bytes are the UTF-16LE high-byte channel.
        //
        int beNullCount = 0;
        int leNullCount = 0;

        for (int pos = 0; pos < buffer.Length; pos += 2)
        {
            if (buffer[pos] == 0)
                beNullCount++;

            if (buffer[pos + 1] == 0)
                leNullCount++;
        }

        double beNullRatio = (double)beNullCount / numUnits;
        double leNullRatio = (double)leNullCount / numUnits;

        //
        // Strong NUL concentration indicates the byte order for
        // Latin-like and mixed text.
        //
        bool beCandidate =
            beNullRatio > 0.5 &&
            leNullRatio < 0.1;

        bool leCandidate =
            leNullRatio > 0.5 &&
            beNullRatio < 0.1;

        if (beCandidate && leCandidate)
            return null;

        //
        // If NUL density is inconclusive, compare the distributions
        // of the two positional byte channels for non-Latin text.
        //
        if (!beCandidate && !leCandidate)
        {
            //
            // Dense native-script text may lack sufficient NUL evidence.
            // Reject ambiguous cases rather than guessing against legacy
            // multibyte encodings such as Shift-JIS, GBK, Big5, or EUC-JP/KR.
            //
            if (beNullRatio < Utf16MinNullFraction &&
                leNullRatio < Utf16MinNullFraction)
            {
                return null;
            }

            // even positions -> BE high-byte channel
            // odd positions  -> LE high-byte channel
            Span<int> evenHistogram = stackalloc int[256];
            Span<int> oddHistogram = stackalloc int[256];

            for (int pos = 0; pos < buffer.Length; pos += 2)
            {
                evenHistogram[buffer[pos]]++;
                oddHistogram[buffer[pos + 1]]++;
            }

            double expected = (double)numUnits / 256;
            double beChi = 0.0;
            double leChi = 0.0;

            for (int i = 0; i < 256; i++)
            {
                double beDiff = evenHistogram[i] - expected;
                double leDiff = oddHistogram[i] - expected;

                beChi += beDiff * beDiff / expected;
                leChi += leDiff * leDiff / expected;
            }

            //
            // Select the channel with the stronger non-uniformity.
            // An exact tie is rejected as ambiguous.
            //
            beCandidate =
                beChi >= Utf16ChannelChiSquareThreshold &&
                beChi > leChi;

            leCandidate =
                leChi >= Utf16ChannelChiSquareThreshold &&
                leChi > beChi;

            if (!beCandidate && !leCandidate)
                return null;
        }

        //
        // Exactly one byte order survives the heuristic analysis.
        //
        UnicodeEncoding candidate =
            leCandidate
                ? Utf16LittleEndianNoBom
                : Utf16BigEndianNoBom;

        //
        // Strict UTF-16 decoding and text-quality validation.
        //
        return TextValidation.IsValidText(candidate, buffer)
            ? candidate
            : null;
    }


    /// <summary>
    /// Determines whether the buffer contains only 7-bit ASCII bytes.
    /// </summary>
    private static bool IsAscii(
        ReadOnlySpan<byte> buffer)
    {
        //
        // ASCII-only data is valid UTF-8, but normally provides insufficient
        // evidence to classify the sample specifically as UTF-8.
        //
        foreach (byte b in buffer)
        {
            if (b != 0x09 &&
                b != 0x0A &&
                b != 0x0D &&
                (b < 0x20 || b > 0x7E))
            {
                return false;
            }
        }

        return true;
    }


    /// <summary>
    /// Detects BOM-less UTF-8 in the specified buffer.
    /// </summary>
    /// <param name="buffer">Buffer to examine.</param>
    /// <returns>
    /// <see cref="UTF8Encoding"/> if valid UTF-8 is detected; otherwise, <see langword="null"/>.
    /// </returns>
    private static UTF8Encoding? CheckUtf8(
        ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
            return null;

        //
        // Strict UTF-8 decoding and text-quality validation.
        //
        return TextValidation.IsValidText(Utf8NoBom, buffer)
            ? Utf8NoBom
            : null;
    }

    #endregion
}
