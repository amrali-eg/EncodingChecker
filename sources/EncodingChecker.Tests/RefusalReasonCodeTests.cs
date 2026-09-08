namespace EncodingChecker.Tests;

/// <summary>
/// The reason EC gives for a decision must come from the decision.
/// </summary>
/// <remarks>
/// <c>ApplyConversion</c> used to work the reason out again from the four raw facts
/// <see cref="ConversionPolicy.Decide"/> had already reduced to a
/// <see cref="SourceInterpretation"/>. The two copies could not disagree — the expressions
/// were identical and their operands never changed between them — but nothing tied them
/// together, and a refusal reason added to the policy would have fallen through to
/// <c>LegacySourceRequired</c> at the call site: a correct refusal carrying the wrong
/// explanation, with no test able to notice. These tests are what now ties them together.
/// </remarks>
public sealed class RefusalReasonCodeTests
{
    private static readonly (string Charset, int CodePage)[] Sources =
    [
        (ScanEngine.UnknownCharset, 0),
        ("windows-1252", 1252),
        ("utf-8", 65001),
        ("utf-16", 1200),
    ];

    private static readonly (string Charset, int CodePage)[] Targets =
    [
        ("utf-16", 1200),
        ("utf-8", 65001),
    ];

    /// <summary>
    /// Every combination of Decide's inputs that <c>ApplyConversion</c> can actually
    /// produce, with the decision it reaches.
    /// </summary>
    /// <remarks>
    /// A conflict needs an explicit source and the automatic doubt needs the absence of
    /// one, so the two are mutually exclusive by construction at the call site.
    /// </remarks>
    private static IEnumerable<(PlannedAction Action, SourceInterpretation Interpretation,
        BomlessUnicodeKind Doubt, bool Conflict)> Reachable()
    {
        foreach ((string sc, int scp) in Sources)
        foreach ((string tc, int tcp) in Targets)
        foreach (bool sourceHasBom in new[] { false, true })
        foreach (bool targetHasBom in new[] { false, true })
        foreach (bool specified in new[] { false, true })
        foreach (bool unicodeOrAscii in new[] { false, true })
        foreach (bool conflict in new[] { false, true })
        foreach (BomlessUnicodeKind doubt in Enum.GetValues<BomlessUnicodeKind>())
        {
            if (conflict && !specified) continue;
            if (doubt != BomlessUnicodeKind.None && specified) continue;

            PlannedAction action = ConversionPolicy.Decide(
                sc, scp, sourceHasBom, tc, tcp, targetHasBom,
                specified, unicodeOrAscii, conflict, doubt,
                out SourceInterpretation interpretation, out _);

            yield return (action, interpretation, doubt, conflict);
        }
    }

    /// <summary>The formula this replaced, kept as the oracle for the change itself.</summary>
    private static string? PreviousFormula(
        PlannedAction action, BomlessUnicodeKind doubt, bool conflict) => action switch
    {
        PlannedAction.Skip => ConversionReasonCodes.UnknownEncoding,
        PlannedAction.Refuse when doubt == BomlessUnicodeKind.Utf32NotProvable =>
            ConversionReasonCodes.UnprovableBomlessUtf32,
        PlannedAction.Refuse when doubt != BomlessUnicodeKind.None =>
            ConversionReasonCodes.AmbiguousBomlessUtf16,
        PlannedAction.Refuse => conflict
            ? ConversionReasonCodes.ExplicitSourceConflictsWithDetection
            : ConversionReasonCodes.LegacySourceRequired,
        _ => null,
    };

    [Fact]
    public void TheReasonIsUnchangedFromTheFormulaItReplaced()
    {
        var checkedCombinations = 0;

        foreach ((PlannedAction action, SourceInterpretation interpretation,
                  BomlessUnicodeKind doubt, bool conflict) in Reachable())
        {
            checkedCombinations++;

            Assert.Equal(
                PreviousFormula(action, doubt, conflict),
                ConversionPolicy.ReasonCodeFor(action, interpretation, doubt));
        }

        // A rule that silently matched nothing would look identical to one that passed.
        Assert.Equal(320, checkedCombinations);
    }

    [Fact]
    public void EveryOutcomeThatLeavesAFileAloneCarriesAReason()
    {
        // The guard that matters for the next refusal reason someone adds: a decision not
        // to convert has to explain itself, and an unmapped interpretation returns null.
        foreach ((PlannedAction action, SourceInterpretation interpretation,
                  BomlessUnicodeKind doubt, _) in Reachable())
        {
            if (action is not (PlannedAction.Refuse or PlannedAction.Skip))
                continue;

            Assert.False(
                string.IsNullOrEmpty(
                    ConversionPolicy.ReasonCodeFor(action, interpretation, doubt)),
                $"{action}/{interpretation}/{doubt} produced no reason code");
        }
    }

    [Fact]
    public void EachRefusalPathHasItsOwnReason()
    {
        // Four ways to refuse, four distinct codes: collapsing any two would tell a user
        // to do something that cannot resolve their case.
        string?[] codes =
        [
            ConversionPolicy.ReasonCodeFor(
                PlannedAction.Refuse, SourceInterpretation.ExplicitSource,
                BomlessUnicodeKind.None),
            ConversionPolicy.ReasonCodeFor(
                PlannedAction.Refuse, SourceInterpretation.AutomaticUnicodeOrAscii,
                BomlessUnicodeKind.Utf16ByteOrderAmbiguous),
            ConversionPolicy.ReasonCodeFor(
                PlannedAction.Refuse, SourceInterpretation.AutomaticUnicodeOrAscii,
                BomlessUnicodeKind.Utf32NotProvable),
            ConversionPolicy.ReasonCodeFor(
                PlannedAction.Refuse, SourceInterpretation.LegacyNeedsSourceChoice,
                BomlessUnicodeKind.None),
        ];

        Assert.Equal(4, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.False(string.IsNullOrEmpty(code)));
    }

    [Fact]
    public void ADecisionToWriteOrLeaveAloneCarriesNoReason()
    {
        // A reason code on a plain conversion would read as a problem in the report.
        // Written as one test rather than a theory because PlannedAction is internal and
        // an InlineData parameter would have to be public.
        foreach (PlannedAction action in
                 new[] { PlannedAction.Convert, PlannedAction.Unchanged })
        foreach (SourceInterpretation interpretation in
                 Enum.GetValues<SourceInterpretation>())
        {
            Assert.Null(ConversionPolicy.ReasonCodeFor(
                action, interpretation, BomlessUnicodeKind.None));
        }
    }
}
