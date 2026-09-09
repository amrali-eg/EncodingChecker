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
            $"Select all did not check {expectedFiles} result row(s). " +
            $"Observed {CheckedResultCount()} checked row(s). " +
            DescribeResultItems());
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
        WaitForMainReady();
    }

    internal void Proceed(AutomationElement review)
    {
        int handle = review.Current.NativeWindowHandle;
        Invoke(review, "btnProceedConversion");
        WaitUntil(() => !WindowExists(handle), "The conversion review did not close.");
        WaitForMainReady();
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
        WaitForMainReady();
    }

    internal bool ReviewContainsControl(AutomationElement review, string automationId) =>
        FindById(review, automationId) is not null;

    /// <summary>Every piece of text the review is showing, joined.</summary>
    /// <remarks>
    /// The advisory is a plain label with no identifier, and giving it one would only
    /// prove the label exists. What matters is the wording a reader actually sees, so
    /// this reads the rendered text rather than a control's presence.
    /// </remarks>
    internal string ReviewText(AutomationElement review) =>
        string.Join(
            "\n",
            review.FindAll(TreeScope.Descendants, Condition.TrueCondition)
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
    internal string StatusText() =>
        string.Join(
            "\n",
            MainWindow.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                .Cast<AutomationElement>()
                .Select(element => element.Current.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name)));

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

        // Timed against real progress rather than a sleep, so the phase does not depend
        // on how fast the machine converts. Cancelling before the first write would
        // exercise the declined-review path instead, which phase A already covers.
        WaitForOperationOutcome(
            () => writingHasBegun() || ConversionHasFinished(),
            "The conversion neither began writing nor reported that it had finished.");

        RequestCancel();

        WaitForStoppedConversion();
    }

    /// <summary>
    /// Requests cancellation, or fails if the run finishes before it can be cancelled.
    /// </summary>
    /// <remarks>
    /// The window hides the button when a run ends - <c>MainForm</c> sets
    /// <c>btnCancel.Visible</c> - so an absent button has two meanings: the run beat the
    /// phase to it, or automation failed to see a button that is on screen. Only the
    /// window's own final status separates them. Either meaning prevents this phase from
    /// proving cancellation, so neither is accepted as a successful test.
    ///
    /// The button and final status are raced so a fast completion produces a clear failure
    /// instead of a misleading timeout.
    ///
    /// Whether cancellation happened is not decided here. Delivering a click is not
    /// proof it took effect, and failing to deliver one is not proof it did not: the
    /// window's own final status is the only thing that separates a run that was stopped
    /// from one that finished on its own, and <see cref="WaitForStoppedConversion"/>
    /// reads it. This method's job is to ask, and to wait until the run has reported
    /// something.
    ///
    /// The two ways a click can fail mean different things. A refusal - the control
    /// reporting itself not-enabled - happens before anything is delivered, so trying
    /// again is safe and right, and it is left to the shared retry loop, which repeats it
    /// and keeps it as the cause a timeout would name.
    ///
    /// Any other automation failure might have followed a click that did land: an element
    /// disappearing mid-call is what a successful cancel looks like when the window hides
    /// the button in response. Clicking again there would be a fresh action rather than a
    /// retry, so the attempt stops and the status is left to say what happened.
    /// </remarks>
    private void RequestCancel()
    {
        bool clickMayHaveLanded = false;

        WaitUntil(
            () =>
            {
                // Once a click may be in flight, stop pressing the button and just watch.
                if (!clickMayHaveLanded)
                {
                    AutomationElement? cancel = FindById(MainWindow, "btnCancel");

                    // Gone means the window hid it, which it does only when a run ends -
                    // but that has to come from the status, not the button's absence.
                    if (cancel is not null)
                    {
                        try
                        {
                            Invoke(cancel);
                            clickMayHaveLanded = true;
                        }
                        // A refusal is the one failure that certainly delivered nothing,
                        // and it is deliberately not caught: the shared loop retries it
                        // and keeps it, so a wait that expires on repeated refusals can
                        // name them. Answering "not ready" here would clear that cause.
                        //
                        // Every other automation failure might have followed a click that
                        // landed, so the attempt stops and the status is left to say.
                        catch (Exception ex) when (
                            ex is not ElementNotEnabledException &&
                            ex is ElementNotAvailableException
                                or COMException
                                or InvalidOperationException)
                        {
                            clickMayHaveLanded = true;
                        }
                    }
                }

                return ConversionHasFinished();
            },
            "The run never reported a final status after cancellation was requested.");
    }

    /// <summary>Requires the cancellation request to produce an interrupted run.</summary>
    private void WaitForStoppedConversion()
    {
        string? finalStatus = null;

        WaitForOperationOutcome(
            () =>
            {
                finalStatus = StatusLine();
                return finalStatus is not null && IsFinalConversionStatus(finalStatus);
            },
            "The conversion did not report a final result after cancellation.");

        if (!finalStatus!.Contains("Conversion stopped", StringComparison.Ordinal))
        {
            throw new GuiDriverException(
                "Cancellation was not exercised. EC instead reported: " + finalStatus);
        }
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

    private void WaitForMainReady() =>
        WaitForOperationOutcome(
            () => FindReviewWindow() is null && ConversionHasFinished(),
            "EncodingChecker did not return to its idle state.");

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
            what + Safely(() => " The status showed: " + StatusText(),
                          " The status could not be read"),
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

            return string.Join(
                "\n",
                bar.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                    .Cast<AutomationElement>()
                    .Select(element => element.Current.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name)));
        }
        catch (Exception ex) when (
            ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// Every way a conversion or preview can end writes one of these, including the paths
    /// where nothing was modified. Matching the headline rather than the counts keeps this
    /// independent of what the run actually did.
    /// </summary>
    private bool ConversionHasFinished()
    {
        if (StatusLine() is not string status)
            return false;

        return IsFinalConversionStatus(status);
    }

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

    private int ResultCount()
    {
        AutomationElement? list = FindById(MainWindow, "lstResults");

        if (list is null)
            return 0;

        AutomationElementCollection children = list.FindAll(
            TreeScope.Children, Condition.TrueCondition);

        return children.Cast<AutomationElement>().Count(element =>
            element.Current.ControlType is var type &&
            (type == ControlType.DataItem || type == ControlType.ListItem));
    }

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
            () => ((TogglePattern)element.GetCurrentPattern(TogglePattern.Pattern))
                      .Current.ToggleState == (value ? ToggleState.On : ToggleState.Off),
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

    private static AutomationElement WaitForElement(
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

    private static void WaitUntil(Func<bool> predicate, string timeoutMessage)
    {
        if (WaitFor(
                () => predicate() ? new object() : null,
                Timeout,
                out Exception? lastError) is not null)
        {
            return;
        }

        throw Expired(timeoutMessage, lastError);
    }

    /// <summary>
    /// A timeout that names the error it kept retrying. A probe that threw every time is
    /// the likeliest reason a wait expired, and every wait in this class reports it.
    /// </summary>
    private static TimeoutException Expired(string message, Exception? lastError) =>
        lastError is null
            ? new TimeoutException(message)
            : new TimeoutException(
                $"{message} Last retried automation error: "
                + $"{lastError.GetType().Name}: {lastError.Message}",
                lastError);

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
