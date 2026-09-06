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
    /// The two flags are mutually exclusive by construction at the call site: a conflict
    /// needs an explicit source, and the automatic ambiguity needs the absence of one.
    /// </remarks>
    private static IEnumerable<(PlannedAction Action, SourceInterpretation Interpretation,
        bool Ambiguous, bool Conflict)> Reachable()
    {
        foreach ((string sc, int scp) in Sources)
        foreach ((string tc, int tcp) in Targets)
        foreach (bool sourceHasBom in new[] { false, true })
        foreach (bool targetHasBom in new[] { false, true })
        foreach (bool specified in new[] { false, true })
        foreach (bool unicodeOrAscii in new[] { false, true })
        foreach (bool conflict in new[] { false, true })
        foreach (bool ambiguous in new[] { false, true })
        {
            if (conflict && !specified) continue;
            if (ambiguous && specified) continue;

            PlannedAction action = ConversionPolicy.Decide(
                sc, scp, sourceHasBom, tc, tcp, targetHasBom,
                specified, unicodeOrAscii, conflict, ambiguous,
                out SourceInterpretation interpretation, out _);

            yield return (action, interpretation, ambiguous, conflict);
        }
    }

    /// <summary>The formula this replaced, kept as the oracle for the change itself.</summary>
    private static string? PreviousFormula(
        PlannedAction action, bool ambiguous, bool conflict) => action switch
    {
        PlannedAction.Skip => ConversionReasonCodes.UnknownEncoding,
        PlannedAction.Refuse when ambiguous => ConversionReasonCodes.AmbiguousBomlessUtf16,
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
                  bool ambiguous, bool conflict) in Reachable())
        {
            checkedCombinations++;

            Assert.Equal(
                PreviousFormula(action, ambiguous, conflict),
                ConversionPolicy.ReasonCodeFor(action, interpretation));
        }

        // A rule that silently matched nothing would look identical to one that passed.
        Assert.Equal(256, checkedCombinations);
    }

    [Fact]
    public void EveryOutcomeThatLeavesAFileAloneCarriesAReason()
    {
        // The guard that matters for the next refusal reason someone adds: a decision not
        // to convert has to explain itself, and an unmapped interpretation returns null.
        foreach ((PlannedAction action, SourceInterpretation interpretation, _, _) in Reachable())
        {
            if (action is not (PlannedAction.Refuse or PlannedAction.Skip))
                continue;

            Assert.False(
                string.IsNullOrEmpty(ConversionPolicy.ReasonCodeFor(action, interpretation)),
                $"{action}/{interpretation} produced no reason code");
        }
    }

    [Fact]
    public void EachRefusalPathHasItsOwnReason()
    {
        // Three ways to refuse, three distinct codes: collapsing any two would tell a user
        // to do something that cannot resolve their case.
        string?[] codes =
        [
            ConversionPolicy.ReasonCodeFor(
                PlannedAction.Refuse, SourceInterpretation.ExplicitSource),
            ConversionPolicy.ReasonCodeFor(
                PlannedAction.Refuse, SourceInterpretation.AutomaticUnicodeOrAscii),
            ConversionPolicy.ReasonCodeFor(
                PlannedAction.Refuse, SourceInterpretation.LegacyNeedsSourceChoice),
        ];

        Assert.Equal(3, codes.Distinct(StringComparer.Ordinal).Count());
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
            Assert.Null(ConversionPolicy.ReasonCodeFor(action, interpretation));
        }
    }
}
