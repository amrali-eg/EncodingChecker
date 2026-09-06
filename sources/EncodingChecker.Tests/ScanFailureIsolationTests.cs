using System.Threading;

namespace EncodingChecker.Tests;

/// <summary>
/// One file's failure must be one row, not the end of the run.
/// </summary>
/// <remarks>
/// The per-item catch named four exception types. Anything outside that list — a
/// <c>SecurityException</c> from an ACL the enumerator did not surface, a regex timeout, a
/// defect in EC itself — escaped <c>Parallel.ForEach</c> as an <c>AggregateException</c>
/// and took every file the run had not reached with it. The CLI's outer catch names the
/// same four, so it would have surfaced as a crash rather than exit 3.
/// <para>
/// Cancellation and <see cref="OutOfMemoryException"/> are deliberately still allowed out:
/// one is the user asking to stop, and continuing after the other would be pretending to
/// process rather than processing.
/// </para>
/// </remarks>
public sealed class ScanFailureIsolationTests
{
    private static ConversionReportEntry Entry(string name) => new()
    {
        FilePath = @"C:\scan\" + name,
        SourceEncoding = "utf-8",
        TargetEncoding = "utf-8",
    };

    private static EntrySink Run(Func<ConversionReportEntry, ConversionReportEntry?> processItem)
    {
        var seen = new EntrySink();

        ScanEngine.RunParallel(
            [Entry("a.txt"), Entry("b.txt"), Entry("c.txt")],
            maxParallelism: 1,
            getPath: entry => entry.FilePath,
            processItem: processItem,
            onEntry: seen.Add,
            CancellationToken.None);

        return seen;
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(System.Security.SecurityException))]
    [InlineData(typeof(System.Text.RegularExpressions.RegexMatchTimeoutException))]
    [InlineData(typeof(FormatException))]
    public void AnUnlistedFailureBecomesOneRowAndTheRunContinues(Type thrown)
    {
        EntrySink seen = Run(entry =>
            entry.FilePath.EndsWith("b.txt", StringComparison.Ordinal)
                ? throw (Exception)Activator.CreateInstance(thrown, "boom")!
                : entry);

        Assert.Equal(3, seen.Count);

        ConversionReportEntry failed = seen.Single(
            e => e.FilePath.EndsWith("b.txt", StringComparison.Ordinal));

        Assert.Equal(ConversionRowResult.Error, failed.Result);
        Assert.Equal(ConversionReasonCodes.ScanFailed, failed.ReasonCode);
        Assert.Equal(PlannedAction.Refuse, failed.Action);
        Assert.False(failed.ReplacementCommitted);
        Assert.Contains("boom", failed.Diagnostic);

        // The other two are untouched, which is the point of isolating the failure.
        Assert.All(
            seen.Where(e => !e.FilePath.EndsWith("b.txt", StringComparison.Ordinal)),
            e => Assert.Null(e.ReasonCode));
    }

    [Theory]
    [InlineData(typeof(OperationCanceledException))]
    [InlineData(typeof(OutOfMemoryException))]
    public void StoppingTheRunIsStillAllowedOut(Type thrown)
    {
        var seen = new EntrySink();

        Assert.ThrowsAny<Exception>(() =>
            ScanEngine.RunParallel(
                [Entry("a.txt")],
                maxParallelism: 1,
                getPath: entry => entry.FilePath,
                processItem: _ => throw (Exception)Activator.CreateInstance(thrown)!,
                onEntry: seen.Add,
                CancellationToken.None));

        // Turned into a row, it would read as one file failing while the run carried on.
        Assert.Equal(0, seen.Count);
    }

    [Fact]
    public void AnEntryThatProcessesToNullIsSimplyNotReported()
    {
        // RefreshSourceSnapshots relies on this, so widening the catch must not change it.
        Assert.Equal(0, Run(_ => null).Count);
    }
}
