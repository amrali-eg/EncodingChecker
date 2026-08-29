using System;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace EncodingChecker;

/// <summary>
/// Provides strict decoding and text-quality validation for byte buffers
/// using a specified character encoding.
/// </summary>
internal static class TextValidation
{
    //
    // Minimum fraction of decoded Unicode scalars that must be text-like
    // for the sample to be accepted as text.
    //
    // REMARKS:
    // Calibrated on multilingual UTF-8/16/32 text, emoji, combining
    // marks, supplementary characters, code/JSON/CSV, and random binary.
    // All real-text samples scored 1.0; 0.9 provides a safety margin.
    //
    private const double MinPrintableFraction = 0.9;


    /// <summary>
    /// Strictly decodes the buffer using the specified encoding and checks
    /// whether the decoded content looks like text.
    /// A successful result establishes that the bytes are compatible with
    /// the encoding and look like text, but does not prove that the encoding
    /// is the original encoding. This distinction is especially important
    /// for single-byte legacy encodings, which may accept the same bytes
    /// under multiple encodings. The test is therefore strong for rejecting
    /// incompatible encodings, but weak for uniquely identifying the
    /// original encoding.
    /// </summary>
    internal static bool IsValidText(
        Encoding encoding,
        ReadOnlySpan<byte> buffer)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        if (buffer.IsEmpty)
            return false;

        int maxChars =
            encoding.GetMaxCharCount(buffer.Length);

        char[] chars =
            ArrayPool<char>.Shared.Rent(maxChars);

        try
        {
            if (!DecodeStrict(
                    encoding,
                    buffer,
                    chars,
                    out int charsWritten))
            {
                return false;
            }

            return LooksLikeText(
                chars.AsSpan(0, charsWritten));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
        }
    }


    /// <summary>
    /// Strictly decodes the specified bytes into a character buffer.
    /// Invalid byte sequences are rejected. An incomplete trailing
    /// sequence is accepted and omitted from the returned string.
    /// </summary>
    private static bool DecodeStrict(
        Encoding encoding,
        ReadOnlySpan<byte> buffer,
        Span<char> chars,
        out int charsWritten)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        charsWritten = 0;

        if (buffer.IsEmpty)
            return false;

        try
        {
            // Force strict decoding regardless of the supplied Encoding instance.
            // A validator treats an unavailable strict codec as invalid; conversion will
            // later report the same condition as a fatal safety failure.
            Decoder decoder = TextEncoding.Strict(encoding).GetDecoder();
            decoder.Fallback = DecoderFallback.ExceptionFallback;

            // Do not flush: the sample may end in the middle of a valid sequence.
            charsWritten = decoder.GetChars(
                buffer,
                chars,
                flush: false);

            return true;
        }
        catch (Exception ex) when (ex is DecoderFallbackException or NotSupportedException)
        {
            return false;
        }
    }


    /// <summary>
    /// Determines whether decoded characters predominantly represent text.
    /// Printable multilingual Unicode, emoji, combining marks, supplementary
    /// characters, and common whitespace, are considered text.
    /// </summary>
    private static bool LooksLikeText(
        ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return false;

        // Limit the heuristic to the first 500 Unicode scalars.
        int runeCount = 0;
        int printable = 0;

        foreach (Rune rune in text.EnumerateRunes())
        {
            if (runeCount >= 500)
                break;

            runeCount++;

            // Common whitespace characters count as printable text.
            if (rune.Value is '\r' or '\n' or '\t' or ' ')
            {
                printable++;
                continue;
            }

            switch (Rune.GetUnicodeCategory(rune))
            {
                // Ignore control and private-use characters in the ratio.
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.Control:
                    break;

                // Treat all other scalars as text-like.
                default:
                    printable++;
                    break;
            }
        }

        if (runeCount == 0)
            return false;

        return (double)printable / runeCount >= MinPrintableFraction;
    }
}
