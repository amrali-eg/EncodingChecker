using System;
using System.IO;
using System.Text;

namespace EncodingChecker;

/// <summary>
/// Prevents automatic conversion when BOM-less UTF-16 byte order cannot be
/// established from the bytes alone.
/// </summary>
internal static class BomlessUnicodeSafety
{
    internal const string AmbiguousReasonCode = "AmbiguousBomlessUtf16";

    /// <summary>
    /// Returns true when the detected BOM-less UTF-16 bytes also strictly
    /// decode under the opposite byte order.
    /// </summary>
    /// <remarks>
    /// Detection may prefer one order, but that preference is not enough to
    /// justify rewriting a file when both orders accept the complete source.
    /// </remarks>
    internal static bool IsAmbiguous(
        Stream source,
        Encoding? detectedEncoding,
        bool detectedHasBom)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (detectedHasBom || detectedEncoding?.CodePage is not (1200 or 1201))
            return false;

        int oppositeCodePage = detectedEncoding.CodePage == 1200 ? 1201 : 1200;
        Encoding opposite = Encoding.GetEncoding(
            oppositeCodePage,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

        return StrictFileValidation.TryValidateStream(source, opposite, out _);
    }

    /// <summary>Builds the actionable explanation shared by reports and the GUI.</summary>
    internal static string DescribeRefusal(Encoding detectedEncoding)
    {
        ArgumentNullException.ThrowIfNull(detectedEncoding);

        (string detectedName, string oppositeName) = Names(detectedEncoding);

        // Both orders are named, and EC's own estimate is not singled out. WebName is
        // "utf-16" for either one, so suggesting it steered the caller straight back
        // into the reading this refusal exists to question - under a name that cannot
        // express the choice being made.
        return $"EC detected BOM-less {detectedName}, but the same bytes are valid as both "
               + $"{detectedName} and {oppositeName}. EC cannot determine the byte order "
               + "safely, so no conversion was performed. Add a byte-order mark, or say "
               + "which order the file uses with -From utf-16le or -From utf-16be.";
    }

    /// <summary>
    /// Describes the same fact for modes that convert nothing.
    /// </summary>
    /// <remarks>
    /// <see cref="DescribeRefusal"/> speaks of a conversion withheld, which would be
    /// meaningless here. The fact reported is the same one.
    /// </remarks>
    internal static string DescribeUnprovableByteOrder(Encoding detectedEncoding)
    {
        ArgumentNullException.ThrowIfNull(detectedEncoding);

        (string detectedName, string oppositeName) = Names(detectedEncoding);

        return $"EC read this file as BOM-less {detectedName}, but the same bytes are "
               + $"equally valid as {oppositeName}. Which one it is cannot be established "
               + "from the file, so the encoding reported here is an estimate rather than "
               + "a finding. Add a byte-order mark to settle it.";
    }

    private static (string Detected, string Opposite) Names(Encoding detectedEncoding) =>
        detectedEncoding.CodePage == 1200
            ? ("UTF-16LE", "UTF-16BE")
            : ("UTF-16BE", "UTF-16LE");
}
