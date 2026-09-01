namespace EncodingChecker;

/// <summary>
/// How EC obtained, or failed to obtain, a safe source interpretation.
/// </summary>
/// <remarks>
/// This replaces the former candidate/ambiguity model. EC automatically converts only
/// sources EC can identify safely; other files need the user's explicit source choice.
/// </remarks>
internal enum SourceInterpretation
{
    /// <summary>The source is Unicode or ASCII and may be converted automatically.</summary>
    AutomaticUnicodeOrAscii,

    /// <summary>The user selected the source codec.</summary>
    ExplicitSource,

    /// <summary>Legacy bytes were detected, but their historical codec was not supplied.</summary>
    LegacyNeedsSourceChoice,

    /// <summary>No source interpretation is needed because EC will not rewrite the file.</summary>
    NotApplicable,
}
