using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace EncodingChecker.GuiSmoke;

/// <summary>Drives the shipped executable through Windows UI Automation.</summary>
internal sealed class EcGuiDriver : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly Process _process;

    /// <summary>
    /// Whether the main window has ever been found. Before it has, a window that
    /// cannot be found is EC failing to show one - which is EC's problem, and must
    /// keep its own failure. Afterwards, the same answer means the window went away.
    /// </summary>
    private readonly bool _windowWasFound;

    internal AutomationElement MainWindow { get; }

    internal EcGuiDriver(string executable)
    {
        _process = Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        }) ?? throw new GuiDriverException("EncodingChecker did not start.");

        try
        {
            _process.WaitForInputIdle(10_000);
        }
        catch (InvalidOperationException)
        {
            // The window lookup below supplies the authoritative startup timeout.
        }

        MainWindow = WaitForElement(
            () => FindTopLevelWindow("MainForm"),
            "EncodingChecker's main window did not appear.");

        _windowWasFound = true;
    }

    internal AutomationElement OpenReview(string directory, int expectedFiles)
    {
        ConfigureScan(directory);
        Scan(expectedFiles);
        return OpenSelectedReview(expectedFiles);
    }

    internal AutomationElement OpenReviewAfterRetarget(
        string scannedDirectory,
        string reviewDirectory,
        int expectedFiles)
    {
        ConfigureScan(scannedDirectory);
        Scan(expectedFiles);
        SetText(MainWindow, "lstBaseDirectory", reviewDirectory);
        return OpenSelectedReview(expectedFiles);
    }

    private void ConfigureScan(string directory)
    {
        SetText(MainWindow, "lstBaseDirectory", directory);
        SetText(MainWindow, "txtFileMasks", "*");
        SetToggle(MainWindow, "chkIncludeSubdirectories", false);
        SetToggle(MainWindow, "chkCreateBackup", true);
        SetToggle(MainWindow, "chkPreviewChanges", false);
        RequireDefaultTarget();
    }

    /// <summary>Fails unless the window opens on utf-8, which every phase assumes.</summary>
    /// <remarks>
    /// The target list is disabled until a scan has run, so this cannot set it - and
    /// asking for the value it already holds would quietly do nothing. MainForm selects
    /// utf-8 only when it finds it, falling back to the first entry otherwise, so the
    /// assumption is worth stating: one clear failure here beats every phase failing in
    /// setup for a reason none of them mentions.
    /// </remarks>
    private void RequireDefaultTarget()
    {
        string target = TargetEncoding();

        if (!string.Equals(target, "utf-8", StringComparison.OrdinalIgnoreCase))
        {
            throw new GuiDriverException(
                $"The target encoding opens on '{target}', not 'utf-8'. Every phase "
                + "converts to utf-8 and none of them sets it.");
        }
    }

    /// <summary>The target encoding the main window currently shows.</summary>
    internal string TargetEncoding() =>
        SelectedName(RequireById(MainWindow, "lstConvert"));

    /// <summary>Sets the target encoding, once a scan has enabled the list.</summary>
    internal void SetTargetEncoding(string value) =>
        SelectCombo(MainWindow, "lstConvert", value);

    private void Scan(int expectedFiles)
    {
        Invoke(MainWindow, "btnView");
        WaitForOperationOutcome(
            () => ResultCount() == expectedFiles && ScanHasFinished(),
            $"View did not finish with {expectedFiles} result row(s).");
    }

    private AutomationElement OpenSelectedReview(int expectedFiles)
    {
        SetToggle(MainWindow, "chkSelectDeselectAll", true);
        WaitUntil(
            () => CheckedResultCount() == expectedFiles,
            () => $"Select all did not check {expectedFiles} result row(s). "
                  + $"Observed {CheckedResultCount()} checked row(s). "
                  + DescribeResultItems());
        Invoke(MainWindow, "btnConvert");

        return WaitForReview();
    }

    internal void TryConfirmSource(
        AutomationElement review,
        string sourceEncoding,
        params string[] filesToCheck)
    {
        foreach (string file in filesToCheck)
            SetRefusedFileChecked(review, file, true);

        SelectCombo(review, "lstSourceEncoding", sourceEncoding);
        Invoke(review, "btnConfirmSourceEncoding");
    }

    internal bool ReviewIsOpen(AutomationElement review) =>
        FindReviewWindow() is { } current &&
        current.Current.NativeWindowHandle == review.Current.NativeWindowHandle;

    internal void WaitForReviewText(AutomationElement review, string expected) =>
        WaitUntil(
            () => ReviewIsOpen(review) &&
                  ReviewText(review).Contains(expected, StringComparison.OrdinalIgnoreCase),
            $"The review did not show '{expected}'.");

    internal AutomationElement ConfirmSource(
        AutomationElement review,
        string sourceEncoding,
        params string[] filesToLeaveUnchecked)
    {
        int oldHandle = review.Current.NativeWindowHandle;

        foreach (string file in filesToLeaveUnchecked)
            SetRefusedFileChecked(review, file, false);

        SelectCombo(review, "lstSourceEncoding", sourceEncoding);
        Invoke(review, "btnConfirmSourceEncoding");

        WaitUntil(
            () => !WindowExists(oldHandle),
            "The source-encoding review did not close.");

        return WaitForReview(oldHandle);
    }

    internal void CancelReview(AutomationElement review)
    {
        int handle = review.Current.NativeWindowHandle;
        Invoke(review, "btnCancelConversionReview");
        WaitUntil(() => !WindowExists(handle), "The conversion review did not close.");
        WaitForMainReady("Conversion cancelled");
    }

    internal void Proceed(AutomationElement review)
    {
        int handle = review.Current.NativeWindowHandle;
        Invoke(review, "btnProceedConversion");
        WaitUntil(() => !WindowExists(handle), "The conversion review did not close.");
        WaitForMainReady("Conversion complete");
    }

    internal void ProceedExpectingWarning(AutomationElement review)
    {
        int handle = review.Current.NativeWindowHandle;
        Invoke(review, "btnProceedConversion");
        WaitUntil(() => !WindowExists(handle), "The conversion review did not close.");

        AutomationElement warning = WaitForElement(
            () => FindProcessWindowByTitle("Warning"),
            "The expected safety warning did not appear.");
        int warningHandle = warning.Current.NativeWindowHandle;

        AutomationElement ok = WaitForElement(
            () => FindNamedControl(warning, ControlType.Button, "OK"),
            "The warning did not expose an OK button.");
        Invoke(ok);

        WaitUntil(() => !WindowExists(warningHandle), "The warning did not close.");
        WaitForMainReady("Conversion did not run");
    }

    internal bool ReviewContainsControl(AutomationElement review, string automationId) =>
        FindById(review, automationId) is not null;

    /// <summary>Every piece of text the review is showing, joined.</summary>
    /// <remarks>
    /// The advisory is a plain label with no identifier, and giving it one would only
    /// prove the label exists. What matters is the wording a reader actually sees, so
    /// this reads the rendered text rather than a control's presence.
    /// </remarks>
    internal string ReviewText(AutomationElement review) => VisibleText(review);

    /// <summary>Every non-blank name under an element, joined one per line.</summary>
    private static string VisibleText(AutomationElement root) =>
        string.Join(
            "\n",
            root.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                .Cast<AutomationElement>()
                .Select(element => element.Current.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name)));

    /// <summary>Every piece of text the main window is showing, joined.</summary>
    /// <remarks>
    /// This is for failure messages, where the whole window is what you want: an
    /// unexpected dialog or an empty result list explains a timeout that the status line
    /// alone would not. Waiting is a different job - see <see cref="StatusLine"/>, which
    /// reads only the status bar and does not throw.
    /// </remarks>
    internal string StatusText() => VisibleText(MainWindow);

    /// <summary>
    /// Starts the conversion and cancels it once the status bar shows progress.
    /// </summary>
    /// <remarks>
    /// Cancelling is timed against EC's own reported progress rather than a sleep, so
    /// the phase does not depend on how fast the machine converts. It deliberately does
    /// not assert how many files were written: that is the run's to decide, and the
    /// checks afterwards compare whatever it reports against the bytes on disk.
    /// </remarks>
    internal void ProceedThenCancel(AutomationElement review, Func<bool> writingHasBegun)
    {
        int handle = review.Current.NativeWindowHandle;
        Invoke(review, "btnProceedConversion");
        WaitUntil(() => !WindowExists(handle), "The conversion review did not close.");

        // Cancelling before the first write would exercise the declined-review path
        // instead, which phase A already covers.
        WaitForOperationOutcome(
            () => writingHasBegun() || ConversionHasFinished(),
            "The conversion neither began writing nor reported that it had finished.");

        CancelAndConfirmStopped();
    }

    /// <summary>
    /// Cancels the run and requires the window to report that it was stopped.
    /// </summary>
    /// <remarks>
    /// One wait decides everything, because the three things that could be asked
    /// separately - has a button appeared, did the press land, what did the run report -
    /// are only meaningful together. Pressing is not proof of cancelling: the window
    /// hides the button when a run ends, so a press can fail precisely because it
    /// worked, and an absent button says only that some run is over. The status is the
    /// single fact that separates a run that was stopped from one that finished on its
    /// own, and EC writes "Conversion stopped" for the first and "Conversion complete"
    /// for the second.
    ///
    /// At most one press with an uncertain outcome is attempted. A press that was
    /// definitely refused may be retried: a refusal - the control reporting itself
    /// not-enabled - is the one failure that certainly delivered nothing, so it is left
    /// to the retry loop, which repeats it and keeps it as a cause. Any other automation
    /// failure might have followed a press that did land, so pressing stops there and
    /// the exception is kept.
    ///
    /// Either way the reason is named in the failure. A run that finishes uncancelled
    /// after a press that was refused, or one whose outcome was unknown, is otherwise
    /// indistinguishable from a machine too fast to interrupt.
    /// </remarks>
    private void CancelAndConfirmStopped()
    {
        bool pressAttempted = false;
        Exception? uncertainPress = null;

        string? finalStatus = WaitFor(
            () =>
            {
                // The status is read first because a refusal throws out of the press
                // below, which would end the attempt before the status was ever looked
                // at. Progress would then depend on the button disappearing rather than
                // on the run reporting, and a button that lingered after the run ended
                // would keep being refused with the answer already on screen.
                if (FinalConversionStatus() is string status)
                    return status;

                if (!pressAttempted)
                {
                    AutomationElement? cancel = FindById(MainWindow, "btnCancel");

                    if (cancel is not null)
                    {
                        try
                        {
                            Invoke(cancel);
                            pressAttempted = true;
                        }
                        catch (Exception ex) when (
                            ex is not ElementNotEnabledException &&
                            ex is ElementNotAvailableException
                                or COMException
                                or InvalidOperationException)
                        {
                            uncertainPress = ex;
                            pressAttempted = true;
                        }
                    }
                }

                return null;
            },
            Timeout,
            out Exception? lastError);

        if (finalStatus is null)
        {
            // Only the uncertain press is added here: Expired already names whatever
            // the wait was still retrying, which is where a refusal shows up.
            throw Expired(
                (pressAttempted
                    ? "A Cancel press was attempted but the run never reported a final status."
                    : "The driver could not find a Cancel button and the run never "
                      + "reported a final status.")
                + Blame(uncertainPress, null),
                lastError);
        }

        // Three outcomes, and only one of them is about cancellation. Collapsing the
        // rest into "cancellation was not exercised" would report a conversion that
        // failed - which EC says outright - as a problem with this phase's timing.
        if (finalStatus.Contains("Conversion stopped", StringComparison.Ordinal))
            return;

        if (finalStatus.Contains("Conversion complete", StringComparison.Ordinal))
        {
            throw new GuiDriverException(
                "Cancellation was not exercised: the run finished before it could be "
                + "stopped. EC reported: " + finalStatus
                + Blame(uncertainPress, lastError));
        }

        throw new GuiDriverException(
            "The conversion neither stopped nor completed, so this phase proved nothing "
            + "about cancellation. EC reported: " + finalStatus
            + Blame(uncertainPress, lastError));
    }

    /// <summary>
    /// Names why the press did not stop the run, so a run that finished uncancelled is
    /// not reported as a machine that was simply too fast.
    /// </summary>
    /// <remarks>
    /// The two are different evidence and read differently. An uncertain press may have
    /// landed and was deliberately not repeated. A refusal certainly delivered nothing
    /// and was retried for as long as the run lasted, and arrives as
    /// <paramref name="lastRefusal"/> - the error the wait was still holding, which it
    /// keeps only when the refusal was the most recent thing to happen.
    /// </remarks>
    private static string Blame(Exception? uncertainPress, Exception? lastRefusal)
    {
        if (uncertainPress is not null)
        {
            return " The Cancel press failed with an unknown outcome and was not repeated: "
                   + $"{uncertainPress.GetType().Name}: {uncertainPress.Message}";
        }

        if (lastRefusal is ElementNotEnabledException)
        {
            return " The last attempt to press Cancel was refused: "
                   + $"{lastRefusal.GetType().Name}: {lastRefusal.Message}";
        }

        return string.Empty;
    }

    /// <summary>Waits until the status line contains <paramref name="fragment"/>.</summary>
    /// <remarks>
    /// The window enables its buttons before it assigns the final status, so a run that
    /// has returned to idle may still be showing the previous message. A phase that reads
    /// the status the moment the buttons come back can therefore read the old one. Waiting
    /// for the text itself closes that window; a sleep would only make it less likely.
    /// </remarks>
    internal void WaitForStatus(string fragment)
    {
        if (WaitFor(
                () => StatusLine()?.Contains(fragment, StringComparison.Ordinal) == true
                    ? new object()
                    : null,
                Timeout,
                out Exception? lastError) is not null)
        {
            return;
        }

        // Read once more here rather than in the message passed in, so the failure shows
        // what was on screen when the wait gave up - and safely, because reading it is
        // another automation call and must not replace the timeout it is describing.
        throw Expired(
            $"The status line never showed '{fragment}'."
            + Safely(() => " It showed: " + StatusText(),
                     " The status could not be read either"),
            lastError);
    }

    /// <summary>
    /// Waits for the window to finish the action just performed and go idle.
    /// </summary>
    /// <remarks>
    /// The caller names the headline its own action produces, rather than this accepting
    /// any final conversion status. A status outlives the action that wrote it - the
    /// window clears it only when the next action starts - so accepting any of them lets
    /// a wait be satisfied by the previous action's report and return before the current
    /// one has finished, so each caller names the headline its own action produces.
    /// </remarks>
    private void WaitForMainReady(string expectedHeadline)
    {
        if (WaitFor(
                () => FindReviewWindow() is null
                      && StatusLine() is string status
                      && status.Contains(expectedHeadline, StringComparison.Ordinal)
                    ? new object()
                    : null,
                Timeout,
                out Exception? lastError) is not null)
        {
            return;
        }

        throw Expired(
            $"EncodingChecker did not go idle: no '{expectedHeadline}' was reported."
            + DescribeStatusSafely()
            + DescribeIdleState(),
            lastError);
    }

    /// <summary>
    /// Refuses the run when EC is alive but its window can no longer be reached.
    /// </summary>
    /// <remarks>
    /// A wait that expires because the window went out of reach has measured nothing
    /// about EC, and reporting it as a phase failure says the opposite. The check is
    /// that the window cannot be found from the desktop at all while the process is
    /// still running: a held element going stale would still leave a fresh lookup
    /// working, and a genuinely hung EC would still leave the window findable.
    ///
    /// IsOffscreen is not the signal - it reads false for a window on another
    /// virtual desktop.
    /// </remarks>
    private void RequireWindowStillReachable()
    {
        if (!_windowWasFound)
            return;

        bool alive;

        try
        {
            alive = !_process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        // EC having exited is EC's business, and the ordinary timeout reports it.
        if (!alive)
            return;

        string lookupFailure = string.Empty;
        bool found;

        try
        {
            found = FindTopLevelWindow("MainForm") is not null;
        }
        catch (Exception ex)
        {
            // The lookup is itself an automation call. Letting it throw would turn the
            // very condition this exists to report back into a phase failure, and a
            // lookup that cannot run is no basis for a verdict about EC either.
            found = false;
            lookupFailure =
                $" Looking the window up again also failed: {ex.GetType().Name}: {ex.Message}.";
        }

        if (found)
            return;

        throw new GuiEnvironmentException(
            "EncodingChecker is still running, but its window can no longer be reached "
            + "through UI Automation, so this phase could not be verified and no verdict "
            + "about EC is reported - it may already have converted files. This is what "
            + "happens when the window is moved to another virtual desktop, or the "
            + "interactive session goes away. Run the suite on the active desktop of an "
            + "interactive Windows session."
            + lookupFailure
            + DescribeIdleState());
    }

    /// <summary>
    /// What could still be established about the window when a wait for idle gave up.
    /// </summary>
    /// <remarks>
    /// A timeout otherwise says only that something expected did not arrive, which is the
    /// position EC-28 left: a phase A failure whose diagnostic showed window chrome and
    /// nothing else, with no way to tell afterwards whether the process had died, the
    /// review was still open, the held main-window element had gone stale while the
    /// window was healthy, or the status bar simply could not be found. These four
    /// separate those, and each is gathered on its own so one unreadable answer does not
    /// cost the others.
    ///
    /// The window is also looked up again from the desktop, because the held element
    /// answering while its subtree holds only chrome has two very different
    /// explanations: the element went stale and a fresh one would work, or the window
    /// itself is unreachable - not on the active desktop, or offscreen - in which case a
    /// fresh lookup fails the same way and retrying anything is pointless.
    /// </remarks>
    private string DescribeIdleState() =>
        " Process alive: " + Ask(() => _process.HasExited ? "no" : "yes")
        + "; main window handle: " + Ask(() => MainWindow.Current.NativeWindowHandle.ToString())
        + "; review present: " + Ask(() => FindReviewWindow() is not null ? "yes" : "no")
        + "; status bar found: "
        + Ask(() => FindById(MainWindow, "statusBar") is not null ? "yes" : "no")
        + "; status bar readable: " + Ask(() => StatusLine() is null ? "no" : "yes")
        + "; window found afresh: " + Ask(() => FindTopLevelWindow("MainForm") is null
            ? "no"
            : "yes")
        + "; handle afresh: " + Ask(() =>
            FindTopLevelWindow("MainForm") is AutomationElement fresh
                ? fresh.Current.NativeWindowHandle.ToString()
                : "n/a")
        + "; status bar via fresh window: " + Ask(() =>
            FindTopLevelWindow("MainForm") is AutomationElement fresh
                && FindById(fresh, "statusBar") is not null
                    ? "yes"
                    : "no")
        + "; window offscreen: " + Ask(() => MainWindow.Current.IsOffscreen ? "yes" : "no")
        + ".";

    /// <summary>One fact for a diagnostic, or why it could not be had.</summary>
    private static string Ask(Func<string> fact)
    {
        try
        {
            return fact();
        }
        catch (Exception ex)
        {
            return "unknown (" + ex.GetType().Name + ")";
        }
    }

    /// <summary>
    /// Waits for something the operation itself produced, rather than for a button.
    /// </summary>
    /// <remarks>
    /// Whether a control is enabled is a different question from whether the work has
    /// finished. The window re-enables its buttons before it writes the result, and its
    /// enabled flag has been seen reporting a control disabled for five seconds while
    /// that same control accepted a click. Rows in the list, bytes on disk and the
    /// summary the window writes when it stops are evidence of the operation; a
    /// control's state is not.
    /// </remarks>
    private void WaitForOperationOutcome(Func<bool> evidence, string what)
    {
        if (WaitFor(() => evidence() ? new object() : null, Timeout, out Exception? lastError)
            is not null)
        {
            return;
        }

        throw Expired(
            what + DescribeStatusSafely(),
            lastError);
    }

    /// <summary>The window writes "N files processed" when a scan ends.</summary>
    private bool ScanHasFinished() =>
        StatusLine() is string status &&
        (status.Contains("files processed", StringComparison.Ordinal) ||
         status.Contains("do not have the correct encoding", StringComparison.Ordinal));

    /// <summary>
    /// The status bar's text, or null when it cannot be read just now.
    /// </summary>
    /// <remarks>
    /// Only the status bar's own subtree is read. Scanning the whole window means walking
    /// every result row - a thousand of them in the interrupted-run phase, while they
    /// are still being added - which is both slow to repeat every 50 ms and prone to
    /// enumerating an element that disappears mid-walk.
    ///
    /// A read that loses that race returns null rather than throwing: not being able to
    /// see the status is not evidence that an operation finished, so a caller waits on.
    /// </remarks>
    private string? StatusLine()
    {
        try
        {
            AutomationElement? bar = FindById(MainWindow, "statusBar");

            if (bar is null)
                return null;

            return VisibleText(bar);
        }
        catch (Exception ex) when (
            ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return null;
        }
    }

    private bool ConversionHasFinished() => FinalConversionStatus() is not null;

    /// <summary>
    /// The status a finished run reported, or null while one is still running.
    /// </summary>
    private string? FinalConversionStatus() =>
        StatusLine() is string status && IsFinalConversionStatus(status) ? status : null;

    /// <summary>
    /// Every way a conversion or preview can end writes one of these, including the paths
    /// where nothing was modified. Matching the headline rather than the counts keeps this
    /// independent of what the run actually did.
    /// </summary>
    private static bool IsFinalConversionStatus(string status) =>
        status.Contains("Conversion complete", StringComparison.Ordinal) ||
        status.Contains("Conversion stopped", StringComparison.Ordinal) ||
        status.Contains("Conversion cancelled", StringComparison.Ordinal) ||
        status.Contains("Conversion did not run", StringComparison.Ordinal) ||
        status.Contains("Conversion failed", StringComparison.Ordinal) ||
        status.Contains("Preview complete", StringComparison.Ordinal) ||
        status.Contains("Preview cancelled", StringComparison.Ordinal);

    private AutomationElement WaitForReview(int previousHandle = 0)
    {
        AutomationElement? review = WaitFor(
            () =>
            {
                AutomationElement? candidate = FindReviewWindow();
                return candidate is not null &&
                       candidate.Current.NativeWindowHandle != previousHandle
                    ? candidate
                    : null;
            },
            Timeout,
            out Exception? lastError);

        if (review is not null)
            return review;

        throw Expired(
            "The conversion review did not appear." + DescribeWindowsSafely(),
            lastError);
    }

    /// <summary>
    /// The window list explains an unexpected modal dialog; the retried error explains why
    /// normal discovery failed. Both are wanted, so listing the windows must never throw:
    /// it is itself an automation call, and the failure it would replace is the more
    /// important one. A listing that fails says why, alongside that error rather than
    /// instead of it.
    /// </summary>
    private string DescribeStatusSafely() =>
        Safely(() => " The status showed: " + StatusText(),
               " The status could not be read");

    private string DescribeWindowsSafely() =>
        Safely(() => " EC exposed these windows: " + DescribeTopLevelWindows(),
               " The windows could not be listed either");

    /// <summary>
    /// Builds a piece of a failure message that is itself an automation call, and never
    /// throws. When automation is what failed, the call gathering detail about it is
    /// likely to fail too, and the detail must never replace the failure it describes.
    /// A part that cannot be gathered says so instead.
    /// </summary>
    private static string Safely(Func<string> describe, string whenItFails)
    {
        try
        {
            return describe();
        }
        catch (Exception ex)
        {
            return $"{whenItFails}: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private AutomationElement? FindReviewWindow() =>
        FindProcessElementById("ConversionConfirmationForm") ??
        FindProcessElementByTitle("Review conversion");

    private int ResultCount() =>
        FindById(MainWindow, "lstResults") is AutomationElement list
            ? ResultItems(list).Count()
            : 0;

    private void SetRefusedFileChecked(
        AutomationElement review,
        string fileName,
        bool value)
    {
        AutomationElement list = RequireById(review, "lstRefusedFiles");
        AutomationElement item = WaitForElement(
            () => FindItem(list, fileName),
            $"The refused-file row '{fileName}' was not found.");

        if (item.TryGetCurrentPattern(TogglePattern.Pattern, out object? rawToggle))
        {
            var toggle = (TogglePattern)rawToggle;
            bool current = toggle.Current.ToggleState == ToggleState.On;

            if (current != value)
                toggle.Toggle();

            return;
        }

        // WinForms exposes check boxes through TogglePattern on supported Windows
        // builds. Keep a real click fallback for older accessibility providers.
        System.Windows.Rect bounds = item.Current.BoundingRectangle;
        ClickAt((int)bounds.Left + 10, (int)(bounds.Top + bounds.Height / 2));
    }

    private static AutomationElement? FindItem(
        AutomationElement list,
        string fileName)
    {
        AutomationElementCollection items = list.FindAll(
            TreeScope.Descendants, Condition.TrueCondition);

        return items.Cast<AutomationElement>().FirstOrDefault(element =>
            (element.Current.ControlType == ControlType.DataItem ||
             element.Current.ControlType == ControlType.ListItem) &&
            element.Current.Name.StartsWith(fileName, StringComparison.OrdinalIgnoreCase));
    }

    private void SetText(
        AutomationElement root,
        string automationId,
        string value)
    {
        AutomationElement element = RequireById(root, automationId);

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? rawValue))
        {
            ((ValuePattern)rawValue).SetValue(value);
            return;
        }

        AutomationElement? edit = element.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Edit));

        if (edit is null ||
            !edit.TryGetCurrentPattern(ValuePattern.Pattern, out rawValue))
        {
            throw new GuiDriverException(
                $"'{automationId}' does not support text input.");
        }

        ((ValuePattern)rawValue).SetValue(value);
    }

    /// <summary>Sets a drop-down list to <paramref name="value"/>.</summary>
    /// <remarks>
    /// Reaching a drop-down item through the tree depends on how the provider exposes the
    /// popup, which varies with its state and the environment, and on which of the two
    /// encoding combos owns a match for the same name. Asking the control for the value
    /// opens no popup and so depends on neither.
    /// </remarks>
    private void SelectCombo(
        AutomationElement root,
        string automationId,
        string value)
    {
        AutomationElement combo = RequireById(root, automationId);

        if (string.Equals(SelectedName(combo), value, StringComparison.OrdinalIgnoreCase))
            return;

        if (!combo.TryGetCurrentPattern(ValuePattern.Pattern, out object? rawValue))
            throw new GuiDriverException($"'{automationId}' cannot be set by value.");

        var setter = (ValuePattern)rawValue;

        if (setter.Current.IsReadOnly)
        {
            throw new GuiDriverException(
                $"'{automationId}' is read-only, so '{value}' cannot be set.");
        }

        setter.SetValue(value);

        WaitUntil(
            () => string.Equals(SelectedName(combo), value, StringComparison.OrdinalIgnoreCase),
            $"'{value}' was not selected in '{automationId}'.");
    }

    private static string SelectedName(AutomationElement combo)
    {
        if (combo.TryGetCurrentPattern(SelectionPattern.Pattern, out object? rawSelection))
        {
            AutomationElement[] selected =
                ((SelectionPattern)rawSelection).Current.GetSelection();

            if (selected.Length > 0)
                return selected[0].Current.Name ?? string.Empty;
        }

        // A provider may hand back null for either of these. Absorbing it here means
        // callers can compare the result without guarding, and an unreadable selection
        // fails their assertion rather than their null check.
        if (combo.TryGetCurrentPattern(ValuePattern.Pattern, out object? rawValue))
            return ((ValuePattern)rawValue).Current.Value ?? string.Empty;

        return string.Empty;
    }

    private void SetToggle(
        AutomationElement root,
        string automationId,
        bool value)
    {
        AutomationElement element = RequireById(root, automationId);

        if (!element.TryGetCurrentPattern(TogglePattern.Pattern, out object? rawToggle))
            throw new GuiDriverException($"'{automationId}' cannot be toggled.");

        var toggle = (TogglePattern)rawToggle;
        bool current = toggle.Current.ToggleState == ToggleState.On;

        if (current != value)
            toggle.Toggle();

        WaitUntil(
            () => toggle.Current.ToggleState == (value ? ToggleState.On : ToggleState.Off),
            $"'{automationId}' did not reach the requested state.");
    }

    private void Invoke(AutomationElement root, string automationId) =>
        Invoke(RequireById(root, automationId));

    private static void Invoke(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out object? rawInvoke))
            throw new GuiDriverException($"'{element.Current.Name}' cannot be invoked.");

        ((InvokePattern)rawInvoke).Invoke();
    }

    private AutomationElement RequireById(
        AutomationElement root,
        string automationId) =>
        WaitForElement(
            () => FindById(root, automationId),
            $"Control '{automationId}' was not found.");

    private static AutomationElement? FindById(
        AutomationElement root,
        string automationId) =>
        root.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.AutomationIdProperty,
                automationId));

    private AutomationElement? FindTopLevelWindow(string automationId)
    {
        var condition = new AndCondition(
            new PropertyCondition(
                AutomationElement.ProcessIdProperty,
                _process.Id),
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Window),
            new PropertyCondition(
                AutomationElement.AutomationIdProperty,
                automationId));

        return AutomationElement.RootElement.FindFirst(TreeScope.Children, condition);
    }

    private AutomationElement? FindProcessElementById(string automationId)
    {
        var condition = new AndCondition(
            new PropertyCondition(
                AutomationElement.ProcessIdProperty,
                _process.Id),
            new PropertyCondition(
                AutomationElement.AutomationIdProperty,
                automationId));

        return AutomationElement.RootElement.FindFirst(
            TreeScope.Descendants,
            condition);
    }

    private AutomationElement? FindProcessElementByTitle(string title)
    {
        var condition = new AndCondition(
            new PropertyCondition(
                AutomationElement.ProcessIdProperty,
                _process.Id),
            new PropertyCondition(
                AutomationElement.NameProperty,
                title));

        return AutomationElement.RootElement.FindFirst(
            TreeScope.Descendants,
            condition);
    }

    private AutomationElement? FindProcessWindowByTitle(string title)
    {
        var condition = new AndCondition(
            new PropertyCondition(
                AutomationElement.ProcessIdProperty,
                _process.Id),
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Window),
            new PropertyCondition(
                AutomationElement.NameProperty,
                title));

        return AutomationElement.RootElement.FindFirst(
            TreeScope.Descendants,
            condition);
    }

    private int CheckedResultCount()
    {
        AutomationElement? list = FindById(MainWindow, "lstResults");

        if (list is null)
            return 0;

        return ResultItems(list).Count(IsChecked);
    }

    private string DescribeResultItems()
    {
        AutomationElement? list = FindById(MainWindow, "lstResults");

        if (list is null)
            return "The results list was not found.";

        string[] items =
        [
            .. ResultItems(list).Select(item =>
                $"'{item.Current.Name}' ({DescribeToggle(item)})")
        ];

        return items.Length == 0
            ? "The results list exposed no rows."
            : "Rows: " + string.Join(", ", items);
    }

    private static IEnumerable<AutomationElement> ResultItems(AutomationElement list) =>
        list.FindAll(TreeScope.Children, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Where(element =>
                element.Current.ControlType == ControlType.DataItem ||
                element.Current.ControlType == ControlType.ListItem);

    private static bool IsChecked(AutomationElement item) =>
        item.TryGetCurrentPattern(TogglePattern.Pattern, out object? rawToggle) &&
        ((TogglePattern)rawToggle).Current.ToggleState == ToggleState.On;

    private static string DescribeToggle(AutomationElement item) =>
        item.TryGetCurrentPattern(TogglePattern.Pattern, out object? rawToggle)
            ? ((TogglePattern)rawToggle).Current.ToggleState.ToString()
            : "no TogglePattern";

    private string DescribeTopLevelWindows()
    {
        AutomationElementCollection windows = AutomationElement.RootElement.FindAll(
            TreeScope.Children,
            new PropertyCondition(
                AutomationElement.ProcessIdProperty,
                _process.Id));

        string[] descriptions =
        [
            .. windows.Cast<AutomationElement>().Select(window =>
                $"'{window.Current.Name}' (id '{window.Current.AutomationId}')")
        ];

        return descriptions.Length == 0 ? "none" : string.Join(", ", descriptions);
    }

    private static AutomationElement? FindNamedControl(
        AutomationElement root,
        ControlType type,
        string name) =>
        root.FindFirst(
            TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    type),
                new PropertyCondition(
                    AutomationElement.NameProperty,
                    name)));

    private AutomationElement? FindWindow(int handle)
    {
        AutomationElementCollection windows = AutomationElement.RootElement.FindAll(
            TreeScope.Children,
            new PropertyCondition(
                AutomationElement.ProcessIdProperty,
                _process.Id));

        return windows.Cast<AutomationElement>().FirstOrDefault(window =>
            window.Current.NativeWindowHandle == handle);
    }

    private bool WindowExists(int handle) => FindWindow(handle) is not null;

    private AutomationElement WaitForElement(
        Func<AutomationElement?> probe,
        string timeoutMessage) =>
        WaitFor(probe, Timeout, out Exception? lastError)
        ?? throw Expired(timeoutMessage, lastError);

    /// <param name="lastError">
    /// The last error retried, when the probe was still failing at the end. A poll that
    /// answers cleanly clears it, so this reports the cause of a wait that kept throwing
    /// rather than one that simply never became true - which is the case a bare timeout
    /// cannot explain by itself.
    /// There is deliberately no overload without it, so a wait that reports a timeout
    /// cannot leave the cause out by accident. Only a caller with nothing to report
    /// discards it, and no wait in this driver currently does.
    /// </param>
    private static T? WaitFor<T>(Func<T?> probe, TimeSpan timeout, out Exception? lastError)
        where T : class
    {
        Stopwatch timer = Stopwatch.StartNew();
        lastError = null;

        while (timer.Elapsed < timeout)
        {
            try
            {
                T? value = probe();

                if (value is not null)
                    return value;

                lastError = null;
            }
            catch (ElementNotAvailableException ex)
            {
                lastError = ex;
            }
            // GuiDriverException is deliberately absent from these: a control that
            // cannot be toggled or set will not start working on the next attempt, and
            // retrying it would turn a precise message into a generic timeout.
            catch (InvalidOperationException ex)
            {
                lastError = ex;
            }
            catch (COMException ex)
            {
                lastError = ex;
            }

            Thread.Sleep(50);
        }

        return null;
    }

    private void WaitUntil(Func<bool> predicate, string timeoutMessage) =>
        WaitUntil(predicate, () => timeoutMessage);

    /// <summary>
    /// Waits, building the failure message only if there is a failure to describe.
    /// </summary>
    /// <remarks>
    /// A message assembled up front runs whatever it interpolates on every passing
    /// call, and an automation call in there can fail a wait whose predicate was
    /// satisfied. It also describes the state before the wait rather than when it
    /// gave up. Building it here fixes both, and routes it through Safely so a
    /// description that throws cannot replace the timeout it exists to explain.
    /// </remarks>
    private void WaitUntil(Func<bool> predicate, Func<string> timeoutMessage)
    {
        if (WaitFor(
                () => predicate() ? new object() : null,
                Timeout,
                out Exception? lastError) is not null)
        {
            return;
        }

        throw Expired(
            Safely(timeoutMessage, "The wait expired and could not be described"),
            lastError);
    }

    /// <summary>
    /// A timeout that names the error it kept retrying. A probe that threw every time is
    /// the likeliest reason a wait expired, and every wait in this class reports it.
    /// </summary>
    private TimeoutException Expired(string message, Exception? lastError)
    {
        // Every timeout in this driver is built here, which makes it the one place that
        // can ask whether EC's window was still reachable when the wait gave up. Asking
        // only in the idle wait was not enough: a desktop excursion during phase C timed
        // out in a control lookup instead, so the check never ran and the phase blamed EC.
        RequireWindowStillReachable();

        return lastError is null
            ? new TimeoutException(message)
            : new TimeoutException(
                $"{message} Last retried automation error: "
                + $"{lastError.GetType().Name}: {lastError.Message}",
                lastError);
    }

    /// <summary>
    /// A failure in the driver's contract with the window - a control that cannot be
    /// toggled, set or invoked - rather than the transient automation errors WaitFor
    /// retries. It deliberately does not derive from InvalidOperationException, which
    /// that retry loop catches, so a deliberate failure surfaces at once with its own
    /// message instead of expiring as an unrelated timeout.
    /// </summary>
    private sealed class GuiDriverException : Exception
    {
        internal GuiDriverException(string message) : base(message)
        {
        }
    }

    private static void ClickAt(int x, int y)
    {
        SetCursorPos(x, y);
        mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5_000);
        }

        _process.Dispose();
    }

    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(
        uint flags,
        uint dx,
        uint dy,
        uint data,
        UIntPtr extraInfo);
}
