using System.Collections.Concurrent;
using System.ComponentModel;

namespace EncodingChecker.Tests;

/// <summary>
/// Pins the BackgroundWorker contract MainForm's convert path depends on.
/// MainForm.ConvertWorkerDoWork sets e.Cancel when the conversion is cancelled, and
/// MainForm.ConvertWorkerCompleted still has to apply whatever was converted before the
/// cancellation. These tests exist because RunWorkerCompletedEventArgs.Result cannot be
/// used to carry those partial results.
/// </summary>
public sealed class GuiWorkerCancellationTests
{
    private static void RunWorker(
        Action<DoWorkEventArgs> doWork,
        Action<RunWorkerCompletedEventArgs> completed)
    {
        using var worker = new BackgroundWorker { WorkerSupportsCancellation = true };
        using var finished = new ManualResetEventSlim(false);

        worker.DoWork += (_, e) => doWork(e);

        worker.RunWorkerCompleted += (_, e) =>
        {
            try
            {
                completed(e);
            }
            finally
            {
                finished.Set();
            }
        };

        worker.RunWorkerAsync();

        Assert.True(finished.Wait(TimeSpan.FromSeconds(10)), "The worker did not complete in time.");
    }

    [Fact]
    public void ResultProperty_WhenCancelled_Throws_WhichIsWhyPartialResultsAreNotCarriedThere()
    {
        // Documents the exact framework behavior behind the bug this class guards:
        // assigning e.Result in DoWork is not enough, because the getter refuses to
        // hand it back once the operation is marked cancelled.
        Exception? observed = null;

        RunWorker(
            doWork: e =>
            {
                e.Cancel = true;
                e.Result = new ConcurrentBag<ConversionReportEntry>();
            },
            completed: e =>
            {
                Assert.True(e.Cancelled);
                Assert.Null(e.Error);

                observed = Record.Exception(() => _ = e.Result);
            });

        Assert.IsType<InvalidOperationException>(observed);
    }

    [Fact]
    public void CancelledConversion_PartialResultsSurviveViaSharedBag_AndCompletionDoesNotThrow()
    {
        // Mirrors MainForm's shape: the bag is created by the UI thread before the worker
        // starts, filled by the worker, and read back after completion regardless of
        // whether the run was cancelled.
        var completedEntries = new ConcurrentBag<ConversionReportEntry>();

        var converted = new ConversionReportEntry
        {
            FilePath = @"C:\somewhere\already-done.txt",
            SourceEncoding = "us-ascii",
            TargetEncoding = "utf-8",
            Result = ConversionRowResult.Converted,
        };

        int appliedCount = -1;
        bool wasCancelled = false;

        RunWorker(
            doWork: e =>
            {
                // One file finished before the user hit Cancel.
                completedEntries.Add(converted);
                e.Cancel = true;
            },
            completed: e =>
            {
                wasCancelled = e.Cancelled;

                // Must not touch e.Result here; reading the shared bag is safe.
                appliedCount = completedEntries.Count;
            });

        Assert.True(wasCancelled);
        Assert.Equal(1, appliedCount);
        Assert.Equal(ConversionRowResult.Converted, Assert.Single(completedEntries).Result);
    }

    [Fact]
    public void ErrorProperty_IsReadableWhenCancelled_SoTheScanPathIsUnaffected()
    {
        // MainForm.ScanWorkerCompleted reads only e.Error/e.Cancelled, neither of which
        // raises - confirming the cancellation defect was specific to the convert path.
        RunWorker(
            doWork: e => e.Cancel = true,
            completed: e =>
            {
                Assert.True(e.Cancelled);
                Assert.Null(e.Error);
            });
    }
}
