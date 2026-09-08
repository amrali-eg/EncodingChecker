using System;
using System.IO;
using System.Text;

namespace EncodingChecker;

/// <summary>
/// Why a BOM-less Unicode file cannot be converted on detection alone.
/// </summary>
/// <remarks>
/// Not persisted. Plans record <see cref="SourceInterpretation"/>, which is unchanged.
/// </remarks>
internal enum BomlessUnicodeKind
{
    /// <summary>The bytes establish the codec, or a BOM does.</summary>
    None,

    /// <summary>UTF-16 whose bytes decode equally well under either byte order.</summary>
    Utf16ByteOrderAmbiguous,

    /// <summary>UTF-32 without a BOM, which detection cannot establish at all.</summary>
    Utf32NotProvable,
}

/// <summary>
/// Prevents automatic conversion when BOM-less Unicode cannot be established from the
/// bytes alone.
/// </summary>
internal static class BomlessUnicodeSafety
{
    internal const string AmbiguousReasonCode = "AmbiguousBomlessUtf16";
    internal const string UnprovableUtf32ReasonCode = "UnprovableBomlessUtf32";

    /// <summary>
    /// Classifies what detection failed to establish about a BOM-less Unicode file.
    /// </summary>
    /// <remarks>
    /// UTF-16 is refused only when the opposite byte order also decodes the whole file,
    /// because otherwise the bytes do establish the order.
    ///
    /// UTF-32 is refused whenever no BOM is present, and an opposite-order test would not
    /// do: UTF-16 text with one character per line puts a C0 control in every second code
    /// unit, and each resulting four-byte group is an in-range unassigned scalar, so it
    /// decodes as UTF-32LE while the *opposite* UTF-32 order rejects it. The unprovable
    /// thing is the codec, not merely its byte order.
    /// </remarks>
    internal static BomlessUnicodeKind Classify(
        Stream source,
        Encoding? detectedEncoding,
        bool detectedHasBom)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (detectedHasBom || detectedEncoding is null)
            return BomlessUnicodeKind.None;

        switch (detectedEncoding.CodePage)
        {
            case 12000 or 12001:
                return BomlessUnicodeKind.Utf32NotProvable;

            case 1200 or 1201:
                int opposite = detectedEncoding.CodePage == 1200 ? 1201 : 1200;

                return StrictFileValidation.TryValidateStream(source, Strict(opposite), out _)
                    ? BomlessUnicodeKind.Utf16ByteOrderAmbiguous
                    : BomlessUnicodeKind.None;

            default:
                return BomlessUnicodeKind.None;
        }
    }

    /// <summary>The machine-readable reason for a <paramref name="kind"/>.</summary>
    internal static string? ReasonCodeFor(BomlessUnicodeKind kind) => kind switch
    {
        BomlessUnicodeKind.Utf16ByteOrderAmbiguous => AmbiguousReasonCode,
        BomlessUnicodeKind.Utf32NotProvable => UnprovableUtf32ReasonCode,
        _ => null,
    };

    /// <summary>Builds the actionable explanation shared by reports and the GUI.</summary>
    internal static string DescribeRefusal(Encoding detectedEncoding) =>
        Describe(detectedEncoding, conversionRefused: true);

    /// <summary>Describes the doubt without implying that conversion was attempted.</summary>
    internal static string DescribeUnprovableByteOrder(Encoding detectedEncoding) =>
        Describe(detectedEncoding, conversionRefused: false);

    private static string Describe(Encoding detectedEncoding, bool conversionRefused)
    {
        ArgumentNullException.ThrowIfNull(detectedEncoding);

        if (detectedEncoding.CodePage is 12000 or 12001)
        {
            string detected = detectedEncoding.CodePage == 12000 ? "UTF-32LE" : "UTF-32BE";

            return $"EC estimates BOM-less {detected}, which it cannot establish from the "
                + "bytes: UTF-16 text can decode as valid UTF-32, and a BOM-less UTF-32 "
                + "byte order cannot be proven either. "
                + (conversionRefused
                    ? "No conversion was performed. Add a byte-order mark, or specify "
                      + "-From with the codec the file was written in."
                    : "Add a byte-order mark to identify it.");
        }

        (string detectedName, string oppositeName) = detectedEncoding.CodePage == 1200
            ? ("UTF-16LE", "UTF-16BE")
            : ("UTF-16BE", "UTF-16LE");

        return $"EC estimates BOM-less {detectedName}, but these bytes are also valid "
            + $"{oppositeName}. The byte order cannot be proven from the file. "
            + (conversionRefused
                ? "No conversion was performed. Add a byte-order mark, or specify "
                  + "-From utf-16le or -From utf-16be."
                : "Add a byte-order mark to identify it.");
    }

    private static Encoding Strict(int codePage) =>
        Encoding.GetEncoding(
            codePage,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
}
