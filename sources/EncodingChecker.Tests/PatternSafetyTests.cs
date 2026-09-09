using System.Text.RegularExpressions;

namespace EncodingChecker.Tests;

/// <summary>
/// A constructed include mask must not be able to stall a scan. Nothing here touches the
/// disk, and the class deliberately carries no fixture, so the assertion can only fail for
/// the reason it exists rather than because a temporary directory misbehaved.
/// </summary>
public sealed class PatternSafetyTests
{
    [Fact]
    public void WildcardPatternsUseTheNonBacktrackingEngine()
    {
        string hostileMask = string.Concat(Enumerable.Repeat("*a", 12)) + "b";

        Regex pattern = Assert.Single(
            DirectoryTraversal.CompilePatterns([hostileMask], defaultToMatchAll: false));

        // Asserted before the match below, so restoring the old engine fails here
        // instead of hanging the test run.
        Assert.True(pattern.Options.HasFlag(RegexOptions.NonBacktracking));

        Assert.DoesNotMatch(pattern, new string('a', 40));
    }
}
