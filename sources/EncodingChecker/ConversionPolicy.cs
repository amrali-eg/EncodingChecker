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
    /// <param name="sourceCodePage">
    /// The source codec's canonical code page, or 0 when the label did not resolve.
    /// </param>
    /// <param name="sourceHasBom">Whether the source file has a BOM.</param>
    /// <param name="targetCharset">The character set to convert to.</param>
    /// <param name="targetCodePage">
    /// The target codec's canonical code page, or 0 when the label did not resolve.
    /// </param>
    /// <param name="targetHasBom">Whether to write a BOM when converting to the target charset.</param>
    /// <param name="sourceWasSpecified">Whether the source encoding was explicitly specified by the user.</param>
    /// <param name="isUnicodeOrAscii">Whether the source encoding is Unicode or ASCII.</param>
    /// <param name="explicitSourceConflictsWithReliableDetection">Whether the explicitly specified source encoding conflicts with reliable detection.</param>
    /// <param name="automaticBomlessUnicodeDoubt">
    /// What automatic detection could not establish about a BOM-less Unicode source:
    /// a UTF-16 byte order the bytes leave open, or UTF-32 they cannot establish at all.
    /// </param>
    /// <returns>The planned action for the file.</returns>
    internal static PlannedAction Decide(
        string sourceCharset,
        int sourceCodePage,
        bool sourceHasBom,
        string targetCharset,
        int targetCodePage,
        bool targetHasBom,
        bool sourceWasSpecified,
        bool isUnicodeOrAscii,
        bool explicitSourceConflictsWithReliableDetection,
        BomlessUnicodeKind automaticBomlessUnicodeDoubt,
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
        //
        // Codec identity is the code page, not the label. "utf-16", "unicode", "ucs-2"
        // and "utf-16le" all name code page 1200, so comparing the strings reported a
        // file already in the target as needing conversion, then rewrote it to identical
        // bytes - discarding its timestamp, and making -FailOnChanges fail forever. A
        // zero code page means the label did not resolve, so it proves nothing.
        if (sourceCodePage != 0
            && sourceCodePage == targetCodePage
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

        if (!sourceWasSpecified &&
            automaticBomlessUnicodeDoubt != BomlessUnicodeKind.None)
        {
            sourceInterpretation = SourceInterpretation.AutomaticUnicodeOrAscii;

            reason = automaticBomlessUnicodeDoubt == BomlessUnicodeKind.Utf32NotProvable
                ? "BOM-less UTF-32 is an estimate the bytes do not establish. Choose the "
                  + "original source encoding explicitly before converting."
                : "BOM-less UTF-16 needs a byte order that the bytes do not prove. "
                  + "Choose the original source encoding explicitly before converting.";

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
    /// <remarks>
    /// JSON can deserialize undefined enum values. Throw rather than report an action no
    /// build wrote as a completed conversion.
    /// </remarks>
    internal static ConversionRowResult ToRowResult(PlannedAction action) => action switch
    {
        PlannedAction.Convert => ConversionRowResult.Converted,
        PlannedAction.Unchanged => ConversionRowResult.Unchanged,
        PlannedAction.Skip => ConversionRowResult.Skipped,
        PlannedAction.Refuse => ConversionRowResult.Refused,
        _ => throw new ArgumentOutOfRangeException(
            nameof(action), action, "Unknown planned action."),
    };

    /// <summary>
    /// The machine-readable reason for a decision, read from the decision itself.
    /// </summary>
    /// <remarks>
    /// Keep this beside <see cref="Decide"/> so a new refusal cannot silently inherit an
    /// unrelated fallback reason.
    /// </remarks>
    internal static string? ReasonCodeFor(
        PlannedAction action,
        SourceInterpretation sourceInterpretation,
        BomlessUnicodeKind bomlessUnicodeDoubt) => (action, sourceInterpretation) switch
    {
        (PlannedAction.Skip, _) => ConversionReasonCodes.UnknownEncoding,

        // Reads the same input Decide read, rather than working the kind out again.
        // Decide reaches this refusal only from a non-None doubt, so a None here is a
        // caller that lost it. Defaulting to the UTF-16 code would rebuild the defect
        // this mapping exists to prevent: a correct refusal carrying a wrong reason,
        // with nothing to fail.
        (PlannedAction.Refuse, SourceInterpretation.AutomaticUnicodeOrAscii) =>
            BomlessUnicodeSafety.ReasonCodeFor(bomlessUnicodeDoubt)
            ?? throw new ArgumentOutOfRangeException(
                nameof(bomlessUnicodeDoubt),
                bomlessUnicodeDoubt,
                "An automatic Unicode refusal must carry the doubt that caused it."),

        (PlannedAction.Refuse, SourceInterpretation.ExplicitSource) =>
            ConversionReasonCodes.ExplicitSourceConflictsWithDetection,

        (PlannedAction.Refuse, SourceInterpretation.LegacyNeedsSourceChoice) =>
            ConversionReasonCodes.LegacySourceRequired,

        _ => null,
    };

    /// <summary>
    /// Whether a refusal can be resolved by the user identifying the original source
    /// encoding. Kept here so the GUI, plans, and CLI describe the same policy.
    /// </summary>
    internal static bool RequiresExplicitSourceChoice(
        SourceInterpretation? sourceInterpretation,
        string? reasonCode) =>
        sourceInterpretation == SourceInterpretation.LegacyNeedsSourceChoice ||
        string.Equals(
            reasonCode,
            ConversionReasonCodes.AmbiguousBomlessUtf16,
            StringComparison.Ordinal) ||
        string.Equals(
            reasonCode,
            ConversionReasonCodes.UnprovableBomlessUtf32,
            StringComparison.Ordinal);
}
