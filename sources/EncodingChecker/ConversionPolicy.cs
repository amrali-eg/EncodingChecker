using System;

namespace EncodingChecker;

/// <summary>
/// The one place that decides what happens to a file.
/// </summary>
/// <remarks>
/// Every surface - CLI, plan, and GUI - uses the same decision instead of reimplementing
/// the safety rules. This keeps conversion behaviour consistent across interfaces.
/// </remarks>
internal static class ConversionPolicy
{
    /// <summary>
    /// Decides what to do with one file, given what is known about its encoding.
    /// </summary>
    /// <param name="sourceInterpretation">How the source encoding was determined.</param>
    /// <param name="reason">
    /// Why, in words a user can act on, when the answer is not a plain conversion.
    /// </param>
    /// <param name="sourceCharset">The character set of the source file.</param>
    /// <param name="sourceHasBom">Whether the source file has a BOM.</param>
    /// <param name="targetCharset">The character set to convert to.</param>
    /// <param name="targetHasBom">Whether to write a BOM when converting to the target charset.</param>
    /// <param name="sourceWasSpecified">Whether the source encoding was explicitly specified by the user.</param>
    /// <param name="isUnicodeOrAscii">Whether the source encoding is Unicode or ASCII.</param>
    /// <param name="explicitSourceConflictsWithReliableDetection">Whether the explicitly specified source encoding conflicts with reliable detection.</param>
    /// <returns>The planned action for the file.</returns>
    internal static PlannedAction Decide(
        string sourceCharset,
        bool sourceHasBom,
        string targetCharset,
        bool targetHasBom,
        bool sourceWasSpecified,
        bool isUnicodeOrAscii,
        bool explicitSourceConflictsWithReliableDetection,
        out SourceInterpretation sourceInterpretation,
        out string? reason)
    {
        reason = null;

        if (string.Equals(
                sourceCharset, ScanEngine.UnknownCharset, StringComparison.Ordinal))
        {
            sourceInterpretation = SourceInterpretation.NotApplicable;
            reason = "The file's encoding could not be identified from its contents.";
            return PlannedAction.Skip;
        }

        // An unchanged file is not read or rewritten, so no source choice is needed.
        if (string.Equals(sourceCharset, targetCharset, StringComparison.OrdinalIgnoreCase)
            && sourceHasBom == targetHasBom)
        {
            sourceInterpretation = SourceInterpretation.NotApplicable;
            return PlannedAction.Unchanged;
        }

        if (sourceWasSpecified && explicitSourceConflictsWithReliableDetection)
        {
            sourceInterpretation = SourceInterpretation.ExplicitSource;
            reason = "The selected source encoding conflicts with EC's reliable Unicode "
                     + "or ASCII detection and could change the text. No conversion was performed.";
            return PlannedAction.Refuse;
        }

        // Automatic conversion is deliberately limited to Unicode and ASCII. The user
        // can name a legacy source with -From or the GUI chooser; all strict decoding,
        // verification, backup, and atomic-install safeguards remain.
        if (!sourceWasSpecified && !isUnicodeOrAscii)
        {
            sourceInterpretation = SourceInterpretation.LegacyNeedsSourceChoice;
            reason = $"'{sourceCharset}' is legacy text. EC converts automatically only "
                     + "from Unicode and ASCII. To convert this file, specify its "
                     + $"original source encoding (for example, -From {sourceCharset}).";
            return PlannedAction.Refuse;
        }

        sourceInterpretation = sourceWasSpecified
            ? SourceInterpretation.ExplicitSource
            : SourceInterpretation.AutomaticUnicodeOrAscii;
        return PlannedAction.Convert;
    }

    /// <summary>
    /// Maps a planned action to its report result.
    /// </summary>
    internal static ConversionRowResult ToRowResult(PlannedAction action) => action switch
    {
        PlannedAction.Unchanged => ConversionRowResult.Unchanged,
        PlannedAction.Skip => ConversionRowResult.Skipped,
        PlannedAction.Refuse => ConversionRowResult.Refused,
        _ => ConversionRowResult.Converted,
    };
}
