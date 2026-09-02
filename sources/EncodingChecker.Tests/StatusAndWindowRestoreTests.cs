using System.Drawing;
using System.Windows.Forms;

namespace EncodingChecker.Tests;

/// <summary>
/// Two things the window tells the user, and one it does to itself.
/// </summary>
public sealed class StatusAndWindowRestoreTests
{
    private static string Describe(bool wasPreview, bool stopped, params ConversionRowResult[] results)
    {
        var tally = new MainForm.ConversionTally();

        foreach (ConversionRowResult result in results)
            tally.Count(result);

        return tally.Describe(wasPreview, stopped);
    }

    [Fact]
    public void SkippedFilesAreNotCountedAsUnchanged()
    {
        // EC-12. Skipped fell into an "everything else" arm, so a file whose encoding
        // EC could not identify was reported as already being in the target encoding -
        // the opposite of what happened. The CLI has always listed them separately.
        string status = Describe(
            wasPreview: false,
            stopped: false,
            ConversionRowResult.Converted,
            ConversionRowResult.Skipped,
            ConversionRowResult.Skipped);

        Assert.Contains("1 converted", status);
        Assert.Contains("2 skipped", status);
        Assert.Contains("0 unchanged", status);
    }

    [Fact]
    public void InvalidFilesAreCountedAsSkippedRatherThanUnchanged()
    {
        // Invalid is a file the run did not convert, not one that needed no conversion.
        string status = Describe(
            wasPreview: false, stopped: false, ConversionRowResult.Invalid);

        Assert.Contains("1 skipped", status);
        Assert.Contains("0 unchanged", status);
    }

    [Fact]
    public void EveryOutcomeIsCountedExactlyOnce()
    {
        // The control. A tally that dropped a category, or counted one twice, would
        // still satisfy the two tests above.
        string status = Describe(
            wasPreview: false,
            stopped: false,
            ConversionRowResult.Converted,
            ConversionRowResult.Unchanged,
            ConversionRowResult.Skipped,
            ConversionRowResult.Invalid,
            ConversionRowResult.Refused,
            ConversionRowResult.Error);

        Assert.Contains("1 converted", status);
        Assert.Contains("1 unchanged", status);
        Assert.Contains("2 skipped", status);
        Assert.Contains("1 refused", status);
        Assert.Contains("1 failed", status);
    }

    [Theory]
    [InlineData(true, true, "Preview cancelled")]
    [InlineData(true, false, "Preview complete")]
    [InlineData(false, true, "Conversion cancelled")]
    [InlineData(false, false, "Conversion complete")]
    public void TheHeadlineSaysWhichKindOfRunEndedAndHow(
        bool wasPreview, bool stopped, string expected)
    {
        Assert.StartsWith(expected, Describe(wasPreview, stopped));
    }

    [Fact]
    public void APreviewSaysWouldBeConvertedRatherThanConverted()
    {
        Assert.Contains(
            "would be converted",
            Describe(wasPreview: true, stopped: false, ConversionRowResult.Converted));
    }

    // ---- CX-12: restoring the window

    private static readonly Rectangle Primary = new(0, 0, 1920, 1080);

    [Fact]
    public void APositionOnAMonitorThatIsGoneIsNotRestored()
    {
        // The window would come back where nothing can reach it, and there is no way
        // back from inside the application.
        var saved = new Rectangle(3000, 400, 900, 700);

        Assert.False(WindowPosition.IsReachable(saved, [Primary]));
    }

    [Fact]
    public void APositionLeftOfThePrimaryMonitorIsRestored()
    {
        // Negative coordinates are ordinary on a multi-monitor setup. Rejecting them
        // outright, as the old check did, threw away good positions on every desktop
        // with a monitor placed left of or above the primary one.
        var secondary = new Rectangle(-1920, 0, 1920, 1080);
        var saved = new Rectangle(-1500, 200, 900, 700);

        Assert.True(WindowPosition.IsReachable(saved, [Primary, secondary]));
    }

    [Fact]
    public void AnOrdinaryPositionIsRestored()
    {
        // The control. A check that refused everything would pass the first test while
        // making EC forget its window position on every launch.
        Assert.True(
            WindowPosition.IsReachable(new Rectangle(100, 100, 900, 700), [Primary]));
    }

    [Fact]
    public void AWindowBarelyOverlappingIsTreatedAsUnreachable()
    {
        // A few pixels of overlap is not a window you can grab hold of.
        var saved = new Rectangle(1900, 1060, 900, 700);

        Assert.False(WindowPosition.IsReachable(saved, [Primary]));
    }

    [Fact]
    public void AWindowSmallerThanTheVisibilityMarginStillCounts()
    {
        // The margin is what must be reachable, or the whole window when it is smaller
        // than that - otherwise a small window could never be restored anywhere.
        var saved = new Rectangle(10, 10, 40, 30);

        Assert.True(WindowPosition.IsReachable(saved, [Primary]));
    }

    [Fact]
    public void NoMonitorsMeansNoRestore()
    {
        Assert.False(WindowPosition.IsReachable(new Rectangle(0, 0, 900, 700), []));
    }

    /// <summary>The bounds a form ends up with after restoring the given position.</summary>
    private static Rectangle Restore(WindowPosition saved, params Rectangle[] screens)
    {
        Rectangle result = Rectangle.Empty;

        UiTest.OnStaThread(() =>
        {
            using var form = new Form();
            form.SetBounds(50, 60, 300, 200);

            saved.ApplyTo(form, screens);

            result = form.Bounds;
        });

        return result;
    }

    [Fact]
    public void RestoringAPositionOnAMissingMonitorLeavesTheFormWhereItIs()
    {
        // IsReachable being right is not enough; ApplyTo has to consult it.
        var saved = new WindowPosition { Left = 3000, Top = 400, Width = 900, Height = 700 };

        Assert.Equal(new Rectangle(50, 60, 300, 200), Restore(saved, Primary));
    }

    [Fact]
    public void RestoringAValidPositionMovesTheForm()
    {
        var saved = new WindowPosition { Left = 120, Top = 130, Width = 900, Height = 700 };

        Assert.Equal(new Rectangle(120, 130, 900, 700), Restore(saved, Primary));
    }

    [Fact]
    public void RestoringANegativePositionOnASecondMonitorMovesTheForm()
    {
        // The old check rejected this outright, discarding a good position on any
        // desktop with a monitor left of or above the primary one.
        var secondary = new Rectangle(-1920, 0, 1920, 1080);
        var saved = new WindowPosition { Left = -1500, Top = 200, Width = 900, Height = 700 };

        Assert.Equal(
            new Rectangle(-1500, 200, 900, 700), Restore(saved, Primary, secondary));
    }

    [Fact]
    public void AnUnsavedPositionLeavesTheFormWhereItIs()
    {
        // The defaults, which mean "nothing was ever saved".
        Assert.Equal(
            new Rectangle(50, 60, 300, 200), Restore(new WindowPosition(), Primary));
    }
}
