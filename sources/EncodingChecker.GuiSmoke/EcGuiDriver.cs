using System.ComponentModel;
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
    private readonly Action<Exception> _reportCleanupError;
    private int _nativeMainWindowHandle;
    private bool _environmentUnavailable;

    internal AutomationElement MainWindow { get; private set; } = null!;

    internal EcGuiDriver(string executable, Action<Exception> reportCleanupError)
    {
        _reportCleanupError = reportCleanupError;
        _process = Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        }) ?? throw new GuiDriverException("EncodingChecker did not start.");

        try
        {
            InitializeWindow();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void InitializeWindow()
    {
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

        _nativeMainWindowHandle = MainWindow.Current.NativeWindowHandle;
    }

    internal GuiReview OpenReview(string directory, int expectedFiles)
    {
        ConfigureScan(directory);
        Scan(expectedFiles);
        return OpenSelectedReview(expectedFiles);
    }

    internal GuiReview OpenReviewAfterRetarget(
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
        ReadMainWindow(
            main => SelectedName(RequireById(main, "lstConvert")),
            "The target encoding could not be read.");

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

    private GuiReview OpenSelectedReview(int expectedFiles)
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
        GuiReview review,
        string sourceEncoding,
        params string[] filesToCheck)
    {
        foreach (string file in filesToCheck)
            SetRefusedFileChecked(review, file, true);

        SelectCombo(review.Element, "lstSourceEncoding", sourceEncoding);
        Invoke(review.Element, "btnConfirmSourceEncoding");
    }

    internal bool ReviewIsOpen(GuiReview review)
    {
        GuiWindowIdentity identity = review.Identity;
        return ReadMainWindow(
            main => FindReviewWindow(main) is { } current &&
                    MatchesReview(current, identity),
            "The conversion review's state could not be read.");
    }

    internal void WaitForReviewText(GuiReview review, string expected)
    {
        GuiWindowIdentity identity = review.Identity;

        if (WaitFor(
                () =>
                {
                    AutomationElement? main = FindTopLevelWindow("MainForm");
                    AutomationElement? current = main is null ? null : FindReviewWindow(main);

                    return current is not null &&
                           MatchesReview(current, identity) &&
                           VisibleText(current).Contains(
                               expected,
                               StringComparison.OrdinalIgnoreCase)
                        ? new object()
                        : null;
                },
                Timeout,
                out Exception? lastError) is not null)
        {
            return;
        }

        throw Expired($"The review did not show '{expected}'.", lastError);
    }

    internal GuiReview ConfirmSource(
        GuiReview review,
        string sourceEncoding,
        params string[] filesToLeaveUnchecked)
    {
        GuiWindowIdentity previous = RequireOpenWindowIdentity(
            review.Element, "source-encoding review");

        foreach (string file in filesToLeaveUnchecked)
            SetRefusedFileChecked(review, file, false);

        SelectCombo(review.Element, "lstSourceEncoding", sourceEncoding);
        Invoke(review.Element, "btnConfirmSourceEncoding");

        WaitForWindowToClose(previous, "The source-encoding review did not close.");

        return WaitForReview(previous);
    }

    internal void CancelReview(GuiReview review)
    {
        GuiWindowIdentity identity = RequireOpenWindowIdentity(review.Element, "conversion review");
        Invoke(review.Element, "btnCancelConversionReview");
        WaitForWindowToClose(identity, "The conversion review did not close.");
        WaitForMainReady("Conversion cancelled");
    }

    internal void Proceed(GuiReview review)
    {
        GuiWindowIdentity identity = RequireOpenWindowIdentity(review.Element, "conversion review");
        Invoke(review.Element, "btnProceedConversion");
        WaitForWindowToClose(identity, "The conversion review did not close.");
        WaitForMainReady("Conversion complete");
    }

    internal void ProceedExpectingWarning(GuiReview review)
    {
        GuiWindowIdentity identity = RequireOpenWindowIdentity(review.Element, "conversion review");
        Invoke(review.Element, "btnProceedConversion");
        WaitForWindowToClose(identity, "The conversion review did not close.");

        AutomationElement warning = WaitForElement(
            () => FindProcessWindowByTitle("Warning"),
            "The expected safety warning did not appear.");
        GuiWindowIdentity warningIdentity = RequireOpenWindowIdentity(warning, "warning");

        AutomationElement ok = WaitForElement(
            () => FindNamedControl(warning, ControlType.Button, "OK"),
            "The warning did not expose an OK button.");
        Invoke(ok);

        WaitForWindowToClose(warningIdentity, "The warning did not close.");
        WaitForMainReady("Conversion did not run");
    }

    internal bool ReviewContainsControl(GuiReview review, string automationId) =>
        ReadReview(
            review,
            current => FindById(current, automationId) is not null,
            $"The conversion review could not be read while looking for '{automationId}'.");

    /// <summary>Every piece of text the review is showing, joined.</summary>
    /// <remarks>
    /// The advisory is a plain label with no identifier, and giving it one would only
    /// prove the label exists. What matters is the wording a reader actually sees, so
    /// this reads the rendered text rather than a control's presence.
    /// </remarks>
    internal string ReviewText(GuiReview review) =>
        ReadReview(
            review,
            VisibleText,
            "The conversion review's text could not be read.");

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
    /// reads only the status bar.
    /// </remarks>
    internal string StatusText() =>
        ReadMainWindow(VisibleText, "The main window's text could not be read.");

    /// <summary>
    /// Starts the conversion and cancels it once the status bar shows progress.
    /// </summary>
    /// <remarks>
    /// Cancelling is timed against EC's own reported progress rather than a sleep, so
    /// the phase does not depend on how fast the machine converts. It deliberately does
    /// not assert how many files were written: that is the run's to decide, and the
    /// checks afterwards compare whatever it reports against the bytes on disk.
    /// </remarks>
    internal void ProceedThenCancel(GuiReview review, Func<bool> writingHasBegun)
    {
        GuiWindowIdentity identity = RequireOpenWindowIdentity(review.Element, "conversion review");
        Invoke(review.Element, "btnProceedConversion");
        WaitForWindowToClose(identity, "The conversion review did not close.");

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
    /// worked. A missing button does not tell us why it disappeared. The status is the
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
        var attempt = new CancellationAttempt(PressAttempted: false);

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

                if (!attempt.PressAttempted)
                {
                    AutomationElement? cancel = FindById(MainWindow, "btnCancel");

                    if (cancel is not null)
                    {
                        try
                        {
                            Invoke(cancel);
                            attempt = attempt with { PressAttempted = true };
                        }
                        catch (Exception ex) when (
                            ex is not ElementNotEnabledException &&
                            ex is ElementNotAvailableException
                                or COMException
                                or InvalidOperationException)
                        {
                            attempt = new CancellationAttempt(PressAttempted: true, UncertainPress: ex);
                        }
                    }
                }

                return null;
            },
            Timeout,
            out Exception? lastError);

        CancellationDecision decision = CancellationPolicy.Decide(finalStatus, attempt, lastError);

        if (decision.Outcome == CancellationOutcome.Stopped)
            return;

        if (decision.Outcome == CancellationOutcome.TimedOut)
            throw Expired(decision.Message, lastError);

        throw new GuiDriverException(decision.Message);
    }

    /// <summary>Waits for EC to report these exact stopped-conversion counts.</summary>
    /// <remarks>
    /// The window enables its buttons before it assigns the final status, so a run that
    /// has returned to idle may still be showing the previous message. A phase that reads
    /// the status the moment the buttons come back can therefore read the old one. Waiting
    /// for the text itself closes that window; a sleep would only make it less likely.
    /// </remarks>
    internal void WaitForStoppedConversionCounts(StoppedConversionCounts expected)
    {
        if (WaitFor(
                () => ConversionStatusText.TryReadStoppedCounts(StatusLine(), out StoppedConversionCounts actual)
                      && actual == expected
                    ? new object()
                    : null,
                Timeout,
                out Exception? lastError) is not null)
        {
            return;
        }

        throw Expired(
            "The status line never reported exactly "
            + $"{expected.Converted} converted and {expected.NotAttempted} not attempted.",
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
    /// one has finished. Each caller therefore names its own expected headline.
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
            $"EncodingChecker did not go idle: no '{expectedHeadline}' was reported.",
            lastError);
    }

    /// <summary>Collects timeout facts once; they are observations, not an atomic snapshot.</summary>
    private WindowObservation ObserveMainWindow()
    {
        var errors = new List<string>();
        bool? processAlive = null;

        try
        {
            processAlive = !_process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            errors.Add(DescribeError("process state", ex));
        }

        int nativeHandle = _nativeMainWindowHandle;

        if (nativeHandle == 0)
        {
            try
            {
                _process.Refresh();
                nativeHandle = _process.MainWindowHandle.ToInt32();
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                errors.Add(DescribeError("native window handle", ex));
            }
        }

        bool? nativeWindowExists = nativeHandle == 0
            ? (errors.Count > 0 ? null : false)
            : WindowBelongsToProcess(nativeHandle);
        AutomationElement? fresh = null;
        bool processWindowFound = false;
        bool lookupFailed = false;

        try
        {
            fresh = FindTopLevelWindow("MainForm");
        }
        catch (Exception ex) when (IsAutomationReadFailure(ex))
        {
            lookupFailed = true;
            errors.Add(DescribeError("fresh window lookup", ex));
        }

        if (fresh is not null)
            processWindowFound = true;
        else
        {
            try
            {
                processWindowFound = FindAnyTopLevelProcessWindow() is not null;
            }
            catch (Exception ex) when (IsAutomationReadFailure(ex))
            {
                lookupFailed = true;
                errors.Add(DescribeError("process window lookup", ex));
            }
        }

        bool? onCurrentDesktop = null;
        if (nativeWindowExists == true)
        {
            try
            {
                onCurrentDesktop = DesktopLocation.IsCurrent((nint)nativeHandle);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                errors.Add(DescribeError("virtual desktop lookup", ex));
            }
        }

        GuiReachability reachability = GuiReachabilityPolicy.Classify(
            fresh is not null,
            processWindowFound,
            processAlive,
            nativeWindowExists,
            lookupFailed,
            onCurrentDesktop);

        bool? reviewPresent = null;
        bool? statusBarFound = null;
        bool? statusBarReadable = null;
        bool? offscreen = null;
        string? status = null;

        if (fresh is not null)
        {
            try
            {
                reviewPresent = FindReviewWindow(fresh) is not null;
            }
            catch (Exception ex) when (IsAutomationReadFailure(ex))
            {
                errors.Add(DescribeError("review lookup", ex));
            }

            try
            {
                AutomationElement? bar = FindById(fresh, "statusBar");
                statusBarFound = bar is not null;

                if (bar is not null)
                {
                    status = VisibleText(bar);
                    statusBarReadable = true;
                }
            }
            catch (Exception ex) when (IsAutomationReadFailure(ex))
            {
                statusBarReadable = false;
                errors.Add(DescribeError("status-bar read", ex));
            }

            try
            {
                offscreen = fresh.Current.IsOffscreen;
            }
            catch (Exception ex) when (IsAutomationReadFailure(ex))
            {
                errors.Add(DescribeError("offscreen state", ex));
            }
        }

        return new WindowObservation(
            reachability,
            processAlive,
            nativeHandle,
            nativeWindowExists,
            processWindowFound,
            reviewPresent,
            statusBarFound,
            statusBarReadable,
            offscreen,
            status,
            errors.AsReadOnly(),
            onCurrentDesktop);
    }

    private static string DescribeError(string operation, Exception error) =>
        $"{operation}: {error.GetType().Name}: {error.Message}";

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
            what,
            lastError);
    }

    /// <summary>The window writes "N files processed" when a scan ends.</summary>
    private bool ScanHasFinished() =>
        StatusLine() is string status &&
        (status.Contains("files processed", StringComparison.Ordinal) ||
         status.Contains("do not have the correct encoding", StringComparison.Ordinal));

    /// <summary>
    /// The status bar's text, or null while it is not exposed.
    /// </summary>
    /// <remarks>
    /// Only the status bar's own subtree is read. Scanning the whole window means walking
    /// every result row - a thousand of them in the interrupted-run phase, while they
    /// are still being added - which is both slow to repeat every 50 ms and prone to
    /// enumerating an element that disappears mid-walk.
    /// Automation failures flow to the wait, which retries them and keeps their cause.
    /// Turning them into null would make a failed read indistinguishable from no status.
    /// </remarks>
    private string? StatusLine()
    {
        AutomationElement? bar = FindById(MainWindow, "statusBar");

        if (bar is null)
            return null;

        return VisibleText(bar);
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

    private GuiReview WaitForReview(GuiWindowIdentity? previous = null)
    {
        GuiReview? review = WaitFor(
            () =>
            {
                AutomationElement? candidate = FindReviewWindow();
                return candidate is not null &&
                       GuiWindowIdentity.IsReplacement(candidate, previous)
                    ? new GuiReview(candidate, CaptureIdentity(candidate))
                    : null;
            },
            Timeout,
            out Exception? lastError);

        if (review is not null)
            return review;

        throw Expired("The conversion review did not appear.", lastError);
    }

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

    /// <summary>Reads through a freshly found main window.</summary>
    private T ReadMainWindow<T>(Func<AutomationElement, T> read, string timeoutMessage)
    {
        ReadResult<T>? result = WaitFor(
            () => FindTopLevelWindow("MainForm") is { } main
                ? new ReadResult<T>(read(main))
                : null,
            Timeout,
            out Exception? lastError);

        return result is not null
            ? result.Value
            : throw Expired(timeoutMessage, lastError);
    }

    /// <summary>Reads an expected review only after finding it again.</summary>
    private T ReadReview<T>(
        GuiReview review,
        Func<AutomationElement, T> read,
        string timeoutMessage)
    {
        GuiWindowIdentity identity = review.Identity;
        ReadResult<T>? result = WaitFor(
            () =>
            {
                AutomationElement? main = FindTopLevelWindow("MainForm");
                AutomationElement? current = main is null ? null : FindReviewWindow(main);

                return current is not null &&
                       MatchesReview(current, identity)
                    ? new ReadResult<T>(read(current))
                    : null;
            },
            Timeout,
            out Exception? lastError);

        return result is not null
            ? result.Value
            : throw Expired(timeoutMessage, lastError);
    }

    /// <summary>The conversion review, if EC currently has one open.</summary>
    /// <remarks>
    /// The review is an owned child of the main window, so it is looked for there rather
    /// than by walking every application on the desktop. Preflight opens a real review
    /// through this method before any phase runs, making that ownership a checked contract.
    ///
    /// Automation errors are deliberately not caught: returning null for one would say
    /// the review is absent when the truth is that nothing could be read, and the waits
    /// that call this already retry and keep the error.
    /// </remarks>
    private AutomationElement? FindReviewWindow() => FindReviewWindow(MainWindow);

    private AutomationElement? FindReviewWindow(AutomationElement main)
    {
        var reviewCondition = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
            new OrCondition(
                new PropertyCondition(
                    AutomationElement.AutomationIdProperty,
                    "ConversionConfirmationForm"),
                new PropertyCondition(
                    AutomationElement.NameProperty,
                    "Review conversion")));

        return main.FindFirst(TreeScope.Children, reviewCondition);
    }

    private sealed record ReadResult<T>(T Value);

    private GuiWindowIdentity CaptureIdentity(AutomationElement window) =>
        new(window.Current.NativeWindowHandle, window.Current.ProcessId, window.GetRuntimeId());

    private static bool MatchesReview(AutomationElement window, GuiWindowIdentity identity) =>
        identity.Matches(window);

    private int ResultCount() =>
        FindById(MainWindow, "lstResults") is AutomationElement list
            ? ResultItems(list).Count()
            : 0;

    private void SetRefusedFileChecked(
        GuiReview review,
        string fileName,
        bool value)
    {
        AutomationElement list = RequireById(review.Element, "lstRefusedFiles");
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

    private static bool IsAutomationReadFailure(Exception error) =>
        error is ElementNotAvailableException or InvalidOperationException or COMException;

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

    private AutomationElement? FindAnyTopLevelProcessWindow() =>
        AutomationElement.RootElement.FindFirst(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, _process.Id));

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

    private GuiWindowIdentity RequireOpenWindowIdentity(AutomationElement window, string description)
    {
        GuiWindowIdentity identity = CaptureIdentity(window);

        if (identity.Handle == 0 || !WindowBelongsToProcess(identity.Handle))
            throw new GuiDriverException($"The {description} was not open before the action.");

        return identity;
    }

    private void WaitForWindowToClose(GuiWindowIdentity identity, string timeoutMessage) =>
        WaitUntil(() => !WindowStillMatches(identity), timeoutMessage);

    private bool WindowStillMatches(GuiWindowIdentity identity)
    {
        if (!WindowBelongsToProcess(identity.Handle))
            return false;

        return identity.Matches(AutomationElement.FromHandle((nint)identity.Handle));
    }

    private AutomationElement WaitForElement(
        Func<AutomationElement?> probe,
        string timeoutMessage) =>
        WaitFor(probe, Timeout, out Exception? lastError)
        ?? throw Expired(timeoutMessage, lastError);

    /// <param name="lastError">
    /// The latest automation error seen while waiting. A later empty read does not erase
    /// it: both facts matter when the expected result never appears.
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
    private Exception Expired(string message, Exception? lastError)
    {
        WindowObservation observation = WindowObservation.Capture(ObserveMainWindow);
        string fullMessage = message
            + (lastError is null
                ? string.Empty
                : " Last observed automation error: "
                  + $"{lastError.GetType().Name}: {lastError.Message}.")
            + observation.Describe();
        var timeout = new GuiWaitException(fullMessage, observation, lastError);

        if (observation.Reachability != GuiReachability.EnvironmentUnavailable)
            return timeout;

        _environmentUnavailable = true;
        return new GuiEnvironmentException(
            "The test could not observe EncodingChecker. Windows confirms its live window "
            + "is on another virtual desktop, and UI Automation cannot see that process. "
            + "The phase is inconclusive. Keep the EC window "
            + "on the active, unlocked Windows desktop and run the phase again. "
            + "Original wait: " + fullMessage,
            observation,
            timeout);
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

    public void Dispose() => GuiCleanup.Run(
        () =>
        {
            if (_process.HasExited)
                return;

            // A handle may have been reused by another process since we recorded it.
            if (_environmentUnavailable &&
                WindowBelongsToProcess(_nativeMainWindowHandle) &&
                PostMessage((nint)_nativeMainWindowHandle, WindowClose, 0, 0) &&
                _process.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                return;
            }

            _process.Kill(entireProcessTree: true);
            if (!_process.WaitForExit(5_000))
                throw new TimeoutException("EncodingChecker did not exit during cleanup.");
        },
        _process.Dispose,
        _reportCleanupError);

    private bool WindowBelongsToProcess(int handle)
    {
        if (handle == 0 || !IsWindow((nint)handle))
            return false;

        uint thread = GetWindowThreadProcessId((nint)handle, out uint owner);
        return GuiWindowIdentity.BelongsToProcess(thread, owner, _process.Id);
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint WindowClose = 0x0010;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

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
