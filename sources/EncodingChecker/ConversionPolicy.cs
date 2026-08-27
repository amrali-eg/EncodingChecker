using System;
using System.Collections.Generic;

namespace EncodingChecker;

/// <summary>
/// The one place that decides what happens to a file.
/// </summary>
/// <remarks>
/// Every surface - the CLI, a written plan, and the GUI - asks this and acts on the
/// answer, rather than each working out for itself what is safe. That is not tidiness.
/// The GUI previously reached its own conclusion by omission: ambiguity was classified
/// only during a Convert-mode scan, and the GUI scans in Detect mode, so every entry
/// arrived at conversion carrying the default "unambiguous" and the refusal that the
/// CLI applied never fired. The tool converted, on the strength of whatever detection
/// returned, the exact files it tells CLI users it will not convert.
/// <para>
/// A safety rule that lives at one call site is a safety rule the next call site does
/// not have.
/// </para>
/// </remarks>
internal static class ConversionPolicy
{
    /// <summary>
    /// Decides what to do with one file, given what is known about its encoding.
    /// </summary>
    /// <param name="reason">
    /// Why, in words a user can act on, when the answer is not a plain conversion.
    /// </param>
    internal static PlannedAction Decide(
        string sourceCharset,
        bool sourceHasBom,
        string targetCharset,
        bool targetHasBom,
        AmbiguityClass ambiguity,
        IReadOnlyList<string> competingEncodings,
        out string? reason)
    {
        reason = null;

        if (string.Equals(
                sourceCharset, ScanEngine.UNKNOWN_CHARSET, StringComparison.Ordinal))
        {
            reason = "The file's encoding could not be identified from its contents.";
            return PlannedAction.Skip;
        }

        // Checked before ambiguity on purpose: a file already in the target encoding is
        // not written at all, so there is no reading of it to get wrong.
        if (string.Equals(sourceCharset, targetCharset, StringComparison.OrdinalIgnoreCase)
            && sourceHasBom == targetHasBom)
        {
            return PlannedAction.Unchanged;
        }

        // The bytes do not identify the encoding that wrote them, and the readings that
        // fit disagree about the text. Detection still produced an answer; acting on it
        // rewrites the file into one of several possible readings without saying so.
        if (ambiguity == AmbiguityClass.TextChanging)
        {
            reason = AmbiguityAnalysis.DescribeRefusal(sourceCharset, competingEncodings);
            return PlannedAction.Refuse;
        }

        return PlannedAction.Convert;
    }

    /// <summary>
    /// How an action reads in a report row.
    /// </summary>
    internal static ConversionRowResult ToRowResult(PlannedAction action) => action switch
    {
        PlannedAction.Unchanged => ConversionRowResult.Unchanged,
        PlannedAction.Skip => ConversionRowResult.Skipped,
        PlannedAction.Refuse => ConversionRowResult.Error,
        _ => ConversionRowResult.Converted,
    };

    /// <summary>
    /// Whether converting this file could change its Unicode content, as opposed to only
    /// re-labelling it.
    /// </summary>
    /// <remarks>
    /// The distinction a confirmation has to draw. A file whose encoding is undetermined
    /// but whose candidate readings all agree on the text - plain ASCII being the common
    /// case - is not something to warn about; a file whose candidates disagree is.
    /// </remarks>
    internal static bool NeedsDisclosure(AmbiguityClass ambiguity) =>
        ambiguity == AmbiguityClass.TextEquivalent;
}
