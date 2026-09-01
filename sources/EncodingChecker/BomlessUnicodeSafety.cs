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

        string detectedName = detectedEncoding.CodePage == 1200
            ? "UTF-16LE"
            : "UTF-16BE";
        string oppositeName = detectedEncoding.CodePage == 1200
            ? "UTF-16BE"
            : "UTF-16LE";

        return $"EC detected BOM-less {detectedName}, but the same bytes are valid as both "
               + $"{detectedName} and {oppositeName}. EC cannot determine the byte order "
               + "safely, so no conversion was performed. Add a byte-order mark or choose "
               + $"the source encoding explicitly (for example, -From {detectedEncoding.WebName}).";
    }
}
