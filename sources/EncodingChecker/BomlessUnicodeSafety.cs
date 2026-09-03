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

        return Describe(detectedName, oppositeName, conversionRefused: true);
    }

    /// <summary>Describes the ambiguity without implying that conversion was attempted.</summary>
    internal static string DescribeUnprovableByteOrder(Encoding detectedEncoding)
    {
        ArgumentNullException.ThrowIfNull(detectedEncoding);

        (string detectedName, string oppositeName) = Names(detectedEncoding);

        return Describe(detectedName, oppositeName, conversionRefused: false);
    }

    private static string Describe(
        string detectedName,
        string oppositeName,
        bool conversionRefused) =>
        $"EC estimates BOM-less {detectedName}, but these bytes are also valid "
        + $"{oppositeName}. The byte order cannot be proven from the file. "
        + (conversionRefused
            ? "No conversion was performed. Add a byte-order mark, or specify "
              + "-From utf-16le or -From utf-16be."
            : "Add a byte-order mark to identify it.");

    private static (string Detected, string Opposite) Names(Encoding detectedEncoding) =>
        detectedEncoding.CodePage == 1200
            ? ("UTF-16LE", "UTF-16BE")
            : ("UTF-16BE", "UTF-16LE");
}
