using System.Text.RegularExpressions;

namespace EncodingChecker.GuiSmoke;

/// <summary>The counts EC reports when a conversion is stopped.</summary>
internal readonly record struct StoppedConversionCounts(int Converted, int NotAttempted);

/// <summary>Reads the stable stopped-conversion summary written by EC.</summary>
internal static class ConversionStatusText
{
    private static readonly Regex StoppedSummary = new(
        @"(?m)^Conversion stopped:\s*(?<converted>\d+)\s+converted,[^\r\n]*,\s*(?<notAttempted>\d+)\s+not attempted\s*$",
        RegexOptions.CultureInvariant);

    internal static bool TryReadStoppedCounts(
        string? status,
        out StoppedConversionCounts counts)
    {
        Match match = status is null ? Match.Empty : StoppedSummary.Match(status);

        if (match.Success &&
            int.TryParse(match.Groups["converted"].Value, out int converted) &&
            int.TryParse(match.Groups["notAttempted"].Value, out int notAttempted))
        {
            counts = new StoppedConversionCounts(converted, notAttempted);
            return true;
        }

        counts = default;
        return false;
    }
}
