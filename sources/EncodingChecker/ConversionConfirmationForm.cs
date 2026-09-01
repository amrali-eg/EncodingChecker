using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace EncodingChecker;

/// <summary>
/// Shows one plain-language review of what EC can and cannot safely convert.
/// </summary>
/// <remarks>
/// Displays the conversion plan already decided by policy. The same entries are
/// converted; no second detection pass can produce a different answer.
/// <para>
/// Files that EC cannot identify safely need an explicit source choice, which never
/// bypasses conversion safety checks.
/// </para>
/// </remarks>
internal sealed class ConversionConfirmationForm : Form
{
    private readonly ConversionPlan _plan;
    private readonly ComboBox _sourceChoice = new();
    private readonly Button _resolve = new();
    private ListView? _refusedList;

    /// <summary>
    /// The source encoding chosen for refused files, or <see langword="null"/> if none
    /// was chosen.
    /// </summary>
    /// <remarks>
    /// Equivalent to <c>-From</c>: it answers which encoding to use without changing
    /// the other conversion safeguards.
    /// </remarks>
    internal string? ChosenSourceEncoding { get; private set; }

    /// <summary>
    /// The refused files <see cref="ChosenSourceEncoding"/> applies to, as full paths.
    /// </summary>
    /// <remarks>
    /// The choice is scoped to the selected files because a batch may contain files
    /// written in different encodings.
    /// </remarks>
    internal IReadOnlyList<string> ChosenFiles { get; private set; } = [];

    internal ConversionConfirmationForm(ConversionPlan plan)
    {
        _plan = plan;

        Text = @"Review conversion";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        // This is a review, not a compact prompt. Leave enough room for the complete
        // explanation and source choice without making the user scroll to understand it.
        ClientSize = new Size(720, 565);
        MinimumSize = new Size(620, 450);

        Controls.Add(BuildBody());
        Controls.Add(BuildButtons());
    }

    private List<PlannedFile> Refused =>
        [.. _plan.Files.Where(f => f is { Action: PlannedAction.Refuse, NeedsSourceChoice: true })];

    private TableLayoutPanel BuildBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(12),
            AutoScroll = true,
        };

        ConversionPlanSummary summary = _plan.Summary;
        int convert = summary.ReadyToConvert;
        List<PlannedFile> refused = Refused;
        List<PlannedFile> advisories =
        [
            .. _plan.Files.Where(f =>
                f.Action == PlannedAction.Convert &&
                f.ReasonCode == ConversionReasonCodes
                    .ExplicitSourceDiffersFromBomlessUnicodeEstimate)
        ];

        body.Controls.Add(Heading(
            "Review this conversion plan before changing files."));

        body.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(660, 0),
            Padding = new Padding(0, 0, 0, 8),
            Text = $"{_plan.Files.Count} selected file(s). Target: "
                + $"{ScanEngine.DescribeTarget(_plan.TargetEncoding, _plan.TargetHasBom)}. "
                + "No files have been changed yet.",
        });

        // The categories are mutually exclusive: every selected file has one outcome.
        body.Controls.Add(Rows(
        [
            ("Ready to convert", convert,
             "EC can verify the conversion."),
            ("Needs a source encoding", refused.Count,
             "Left unchanged unless you choose one below."),
            ("Already in the target encoding", summary.AlreadyTarget,
             "No conversion is needed."),
            ("Encoding not identified", summary.NotIdentified,
             "Left unchanged because EC could not identify text."),
            ("Cannot be processed safely", summary.OtherRefusals,
             "Left unchanged because a safety check did not pass."),
        ]));

        if (advisories.Count > 0)
        {
            string examples = string.Join(
                Environment.NewLine,
                advisories.Take(5).Select(f => $"• {f.RelativePath}: {f.Reason}"));

            if (advisories.Count > 5)
                examples += Environment.NewLine + $"• and {advisories.Count - 5} more";

            body.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(660, 0),
                Padding = new Padding(0, 8, 0, 8),
                ForeColor = Color.FromArgb(150, 80, 0),
                Text =
                    $"{advisories.Count} file(s) have a BOM-less Unicode estimate that "
                    + "differs from your source choice. EC will use your choice, but review "
                    + "these files carefully:" + Environment.NewLine + examples,
            });
        }

        body.Controls.Add(Rule());

        body.Controls.Add(Rows(
        [
            ("Directory", _plan.BaseDirectory),
            ("Source encoding",
             string.IsNullOrEmpty(_plan.ExplicitSourceEncoding)
                 ? "EC converts automatically only when it can identify the source safely"
                 : $"{_plan.ExplicitSourceEncoding} (chosen by you; strict checks still apply)"),
            ("Backups", _plan.BackupEnabled
                ? "enabled — original as <file>.bak; record as <file>.ecmeta.json"
                : "OFF — originals will not be kept"),
            ("Before any replacement", "strict decoding and output verification must succeed"),
        ]));

        if (refused.Count > 0)
            body.Controls.Add(BuildRefusalPanel(refused));

        foreach (Control control in body.Controls)
            control.Dock = DockStyle.Top;

        // Docked children stack in reverse order.
        Control[] ordered = [.. body.Controls.Cast<Control>().Reverse()];
        body.Controls.Clear();
        body.Controls.AddRange(ordered);

        return body;
    }

    private Panel BuildRefusalPanel(List<PlannedFile> refused)
    {
        var panel = new Panel { AutoSize = true, Padding = new Padding(0, 12, 0, 0) };

        var explanation = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(660, 0),
            ForeColor = Color.FromArgb(150, 40, 0),
            Text =
                $@"{refused.Count} file(s) require their source encoding to be identified or confirmed. "
                + @"Encoding Checker cannot safely process these files until you specify or confirm their original encoding."
                + Environment.NewLine + Environment.NewLine
                + @"Select only files that use the same source encoding, then choose or confirm that encoding. "
                + @"Leave files with a different or unknown encoding unchecked; you can review them later.",
        };

        var list = new ListView
        {
            AccessibleName = "Files requiring source encoding",
            AccessibleDescription = "Select files that share the source encoding chosen below.",
            View = View.Details,
            FullRowSelect = true,
            CheckBoxes = true,
            Height = 120,
            Dock = DockStyle.Top,
        };

        list.Columns.Add("File", 365);
        list.Columns.Add("Detected encoding", 165);

        // Show every refused file so the stated scope and the user's selection agree.
        foreach (PlannedFile file in refused)
        {
            list.Items.Add(new ListViewItem(
            [
                file.RelativePath,
                file.SourceEncoding,
            ])
            {
                Checked = true,
                Tag = _plan.ResolvePath(file),
            });
        }

        _refusedList = list;
        list.ItemChecked += (_, _) => UpdateScopeLabel();

        var chooser = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 8, 0, 0),
        };

        chooser.Controls.Add(new Label
        {
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 0),
            Text = @"Source encoding for the ticked files:",
        });

        _sourceChoice.DropDownStyle = ComboBoxStyle.DropDownList;
        _sourceChoice.AccessibleName = "Source encoding for selected files";
        _sourceChoice.AccessibleDescription = "Choose the original encoding for the ticked files.";
        _sourceChoice.Width = 235;
        _sourceChoice.Items.Add("Choose or confirm source encoding…");

        // Offer only codecs the current runtime can actually use.
        foreach (Encoding encoding in TextEncoding.SupportedEncodings)
            _sourceChoice.Items.Add(encoding.WebName);

        _sourceChoice.SelectedIndex = 0;
        _sourceChoice.SelectedIndexChanged += (_, _) => UpdateScopeLabel();

        _resolve.AutoSize = true;
        _resolve.AccessibleName = "Confirm selected source encoding";
        _resolve.AccessibleDescription = "Rebuild the review using the chosen encoding for the ticked files.";
        // Native themed buttons do not consistently add Padding to their preferred
        // width. A minimum width gives the action a stable, readable target instead.
        _resolve.MinimumSize = new Size(230, 0);
        _resolve.Click += (_, _) =>
        {
            ChosenSourceEncoding = (string)_sourceChoice.SelectedItem!;
            ChosenFiles = TickedFiles();
            DialogResult = DialogResult.Retry;
            Close();
        };

        UpdateScopeLabel();

        chooser.Controls.Add(_sourceChoice);
        chooser.Controls.Add(_resolve);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(660, 0),
            ForeColor = SystemColors.GrayText,
            Dock = DockStyle.Top,
            Text =
                "Choosing an encoding does not convert anything yet. EC will refresh this "
                + "review first; you then decide whether to convert the newly ready files. "
                + "The chosen bytes must still decode strictly; backup and output "
                + "verification failures still stop the conversion.",
        };

        panel.Controls.Add(note);
        panel.Controls.Add(chooser);
        panel.Controls.Add(list);
        panel.Controls.Add(explanation);

        explanation.Dock = DockStyle.Top;

        return panel;
    }

    /// <summary>
    /// The currently selected refused files, as full paths.
    /// </summary>
    private List<string> TickedFiles() =>
    [
        .. (_refusedList?.CheckedItems.Cast<ListViewItem>()
            ?? [])
            .Select(i => i.Tag as string)
            .Where(p => p is not null)
            .Select(p => p!)
    ];

    /// <summary>
    /// Keeps the button scope explicit.
    /// </summary>
    private void UpdateScopeLabel()
    {
        int ticked = _refusedList?.CheckedItems.Count ?? 0;

        _resolve.Text = $@"Confirm for {ticked} file(s)";
        _resolve.Enabled = ticked > 0 && _sourceChoice.SelectedIndex > 0;
    }

    private FlowLayoutPanel BuildButtons()
    {
        var strip = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12),
            AutoSize = true,
        };

        var cancel = new Button
        {
            AccessibleDescription = "Close this review without changing files.",
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
        };

        int convert = _plan.Summary.ReadyToConvert;
        int refused = Refused.Count;

        var proceed = new Button
        {
            AccessibleDescription = "Convert only the files marked ready in this review.",
            Text = convert == 0
                ? "Nothing ready to convert"
                : refused == 0
                    ? $"Convert {convert} file(s)"
                    : $"Convert {convert} ready file(s)",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Enabled = convert > 0,
        };

        strip.Controls.Add(cancel);
        strip.Controls.Add(proceed);

        AcceptButton = proceed;
        CancelButton = cancel;

        return strip;
    }

    private static Label Heading(string text) => new()
    {
        AutoSize = true,
        Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold),
        MaximumSize = new Size(660, 0),
        Padding = new Padding(0, 0, 0, 8),
        Text = text,
    };

    private static Label Rule() => new()
    {
        BorderStyle = BorderStyle.Fixed3D,
        Height = 2,
        Margin = new Padding(0, 8, 0, 8),
    };

    private static TableLayoutPanel Rows(IReadOnlyList<(string Label, int Count, string Note)> rows)
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = rows.Count,
            AutoSize = true,
        };

        foreach ((string label, int count, string note) in rows)
        {
            // Hide empty categories so the summary stays focused.
            if (count == 0)
                continue;

            table.Controls.Add(new Label
            {
                AutoSize = true,
                Text = count.ToString(),
                Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight,
                Width = 44,
            });

            table.Controls.Add(new Label { AutoSize = true, Text = label });
            table.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Text = note,
            });
        }

        return table;
    }

    private static TableLayoutPanel Rows(IReadOnlyList<(string Label, string Value)> rows)
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = rows.Count,
            AutoSize = true,
        };

        foreach ((string label, string value) in rows)
        {
            table.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Text = label,
            });

            table.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(480, 0),
                Text = value,
            });
        }

        return table;
    }
}
