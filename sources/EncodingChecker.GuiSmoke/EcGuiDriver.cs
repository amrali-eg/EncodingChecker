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
        }) ?? throw new InvalidOperationException("EncodingChecker did not start.");

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
        SetText(MainWindow, "lstBaseDirectory", directory);
        SetText(MainWindow, "txtFileMasks", "*");
        SetToggle(MainWindow, "chkIncludeSubdirectories", false);
        SetToggle(MainWindow, "chkCreateBackup", true);
        SetToggle(MainWindow, "chkPreviewChanges", false);
        SelectCombo(MainWindow, "lstConvert", "utf-8");

        Invoke(MainWindow, "btnView");
        WaitUntil(
            () => ResultCount() == expectedFiles && IsEnabled(MainWindow, "btnView"),
            $"View did not finish with {expectedFiles} result row(s).");

        SetToggle(MainWindow, "chkSelectDeselectAll", true);
        WaitUntil(
            () => CheckedResultCount() == expectedFiles,
            $"Select all did not check {expectedFiles} result row(s). " +
            $"Observed {CheckedResultCount()} checked row(s). " +
            DescribeResultItems());
        Invoke(MainWindow, "btnConvert");

        return WaitForReview();
    }

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

    private void WaitForMainReady() =>
        WaitUntil(
            () => IsEnabled(MainWindow, "btnView") && FindReviewWindow() is null,
            "EncodingChecker did not return to its idle state.");

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
            Timeout);

        return review ?? throw new TimeoutException(
            "The conversion review did not appear. EC exposed these windows: "
            + DescribeTopLevelWindows());
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
            throw new InvalidOperationException(
                $"'{automationId}' does not support text input.");
        }

        ((ValuePattern)rawValue).SetValue(value);
    }

    private void SelectCombo(
        AutomationElement root,
        string automationId,
        string value)
    {
        AutomationElement combo = RequireById(root, automationId);

        if (SelectedName(combo).Equals(value, StringComparison.OrdinalIgnoreCase))
            return;

        if (combo.TryGetCurrentPattern(
                ExpandCollapsePattern.Pattern,
                out object? rawExpand))
        {
            ((ExpandCollapsePattern)rawExpand).Expand();
        }

        AutomationElement? item = WaitFor(
            () => FindNamedItem(combo, value) ?? FindProcessItem(value),
            TimeSpan.FromSeconds(5));

        if (item is not null &&
            item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? rawSelection))
        {
            ((SelectionItemPattern)rawSelection).Select();
        }
        else
        {
            SetForegroundWindow(MainWindow.Current.NativeWindowHandle);
            combo.SetFocus();
            System.Windows.Forms.SendKeys.SendWait(value);
            System.Windows.Forms.SendKeys.SendWait("{ENTER}");
        }

        WaitUntil(
            () => SelectedName(combo).Equals(value, StringComparison.OrdinalIgnoreCase),
            $"'{value}' was not selected in '{automationId}'.");
    }

    private AutomationElement? FindNamedItem(
        AutomationElement root,
        string value)
    {
        AutomationElementCollection items = root.FindAll(
            TreeScope.Descendants, Condition.TrueCondition);

        return items.Cast<AutomationElement>().FirstOrDefault(element =>
            element.Current.ControlType == ControlType.ListItem &&
            element.Current.Name.Equals(value, StringComparison.OrdinalIgnoreCase) &&
            !element.Current.IsOffscreen);
    }

    private AutomationElement? FindProcessItem(string value)
    {
        var condition = new AndCondition(
            new PropertyCondition(
                AutomationElement.ProcessIdProperty,
                _process.Id),
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.ListItem));

        AutomationElementCollection items = AutomationElement.RootElement.FindAll(
            TreeScope.Descendants, condition);

        return items.Cast<AutomationElement>().FirstOrDefault(element =>
            element.Current.Name.Equals(value, StringComparison.OrdinalIgnoreCase) &&
            !element.Current.IsOffscreen);
    }

    private static string SelectedName(AutomationElement combo)
    {
        if (combo.TryGetCurrentPattern(SelectionPattern.Pattern, out object? rawSelection))
        {
            AutomationElement[] selected =
                ((SelectionPattern)rawSelection).Current.GetSelection();

            if (selected.Length > 0)
                return selected[0].Current.Name;
        }

        if (combo.TryGetCurrentPattern(ValuePattern.Pattern, out object? rawValue))
            return ((ValuePattern)rawValue).Current.Value;

        return string.Empty;
    }

    private void SetToggle(
        AutomationElement root,
        string automationId,
        bool value)
    {
        AutomationElement element = RequireById(root, automationId);

        if (!element.TryGetCurrentPattern(TogglePattern.Pattern, out object? rawToggle))
            throw new InvalidOperationException($"'{automationId}' cannot be toggled.");

        var toggle = (TogglePattern)rawToggle;
        bool current = toggle.Current.ToggleState == ToggleState.On;

        if (current != value)
            toggle.Toggle();

        WaitUntil(
            () => ((TogglePattern)element.GetCurrentPattern(TogglePattern.Pattern))
                      .Current.ToggleState == (value ? ToggleState.On : ToggleState.Off),
            $"'{automationId}' did not reach the requested state.");
    }

    private bool IsEnabled(AutomationElement root, string automationId) =>
        FindById(root, automationId)?.Current.IsEnabled == true;

    private void Invoke(AutomationElement root, string automationId) =>
        Invoke(RequireById(root, automationId));

    private static void Invoke(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out object? rawInvoke))
            throw new InvalidOperationException($"'{element.Current.Name}' cannot be invoked.");

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
        WaitFor(probe, Timeout) ?? throw new TimeoutException(timeoutMessage);

    private static T? WaitFor<T>(Func<T?> probe, TimeSpan timeout)
        where T : class
    {
        Stopwatch timer = Stopwatch.StartNew();

        while (timer.Elapsed < timeout)
        {
            try
            {
                T? value = probe();

                if (value is not null)
                    return value;
            }
            catch (ElementNotAvailableException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (COMException)
            {
            }

            Thread.Sleep(50);
        }

        return null;
    }

    private static void WaitUntil(Func<bool> predicate, string timeoutMessage)
    {
        if (WaitFor(
                () => predicate() ? new object() : null,
                Timeout) is null)
        {
            throw new TimeoutException(timeoutMessage);
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
    private static extern bool SetForegroundWindow(int hWnd);

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
