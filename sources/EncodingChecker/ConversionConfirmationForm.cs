using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace EncodingChecker;

/// <summary>
/// Shows what a conversion is about to do, and asks.
/// </summary>
/// <remarks>
/// Displays a plan that has already been decided rather than working out the answer for
/// itself. That is the whole point: a dialog that describes one set of conclusions and a
/// conversion that reaches its own is a dialog that can be wrong, and the user approved
/// what the dialog said. The same entries that were classified here are the ones that
/// convert - no second detection pass, the same property <c>-Apply</c> has.
/// <para>
/// The three outcomes it has to keep apart are the ones the classification draws:
/// an encoding the bytes determine, several codecs that agree on the text, and several
/// codecs that disagree. Only the third is refused, and for it the dialog names the
/// alternatives and offers the one thing that resolves it - saying which encoding it is.
/// </para>
/// </remarks>
internal sealed class ConversionConfirmationForm : Form
{
    private readonly ConversionPlan _plan;
    private readonly ComboBox _sourceChoice = new();
    private readonly Button _resolve = new();

    /// <summary>
    /// The encoding the user chose for the refused files, or <see langword="null"/> if
    /// they did not choose one.
    /// </summary>
    /// <remarks>
    /// Exactly what <c>-From</c> supplies on the command line: an answer to "which
    /// encoding is this?", replacing detection and nothing else. Every conversion
    /// safeguard still applies to the files it is used for.
    /// </remarks>
    internal string? ChosenSourceEncoding { get; private set; }

    internal ConversionConfirmationForm(ConversionPlan plan)
    {
        _plan = plan;

        Text = "Confirm conversion";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(680, 520);
        MinimumSize = new Size(560, 420);

        Controls.Add(BuildBody());
        Controls.Add(BuildButtons());
    }

    private int Count(PlannedAction action) =>
        _plan.Files.Count(f => f.Action == action);

    private List<PlannedFile> Refused =>
        [.. _plan.Files.Where(f => f is { Action: PlannedAction.Refuse, MayChangeText: true })];

    private Control BuildBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(12),
            AutoScroll = true,
        };

        int convert = Count(PlannedAction.Convert);
        int equivalent = _plan.Files.Count(
            f => f.Action == PlannedAction.Convert
                 && f.Ambiguity == AmbiguityClass.TextEquivalent);

        List<PlannedFile> refused = Refused;

        body.Controls.Add(Heading(
            $"Convert {convert} of {_plan.Files.Count} selected file(s) to "
            + _plan.TargetEncoding
            + (_plan.TargetHasBom ? " with BOM" : " without BOM")));

        // The three states, kept apart. The counts sum to the selected population.
        body.Controls.Add(Rows(
        [
            ("Encoding determined by the file's own bytes", convert - equivalent,
             "Will convert."),
            ("Encoding undetermined, every reading agrees on the text", equivalent,
             "Will convert; the label is a choice, the content is not."),
            ("Encoding undetermined, readings disagree on the text", refused.Count,
             "WILL NOT be converted."),
            ("Already in the target encoding", Count(PlannedAction.Unchanged),
             "Nothing to do."),
            ("Encoding could not be identified", Count(PlannedAction.Skip),
             "Left alone."),
            ("Could not be read", Count(PlannedAction.Refuse) - refused.Count,
             "Left alone."),
        ]));

        body.Controls.Add(Rule());

        body.Controls.Add(Rows(
        [
            ("Directory", _plan.BaseDirectory),
            ("Source encoding",
             string.IsNullOrEmpty(_plan.ExplicitSourceEncoding)
                 ? "detected per file"
                 : $"{_plan.ExplicitSourceEncoding} (chosen; detection bypassed)"),
            ("Backups", _plan.BackupEnabled
                ? "enabled — each original kept as <file>.bak"
                : "DISABLED — originals will not be kept"),
            ("Guarantees", ConversionSemantics.Describes),
        ]));

        if (refused.Count > 0)
            body.Controls.Add(BuildRefusalPanel(refused));

        foreach (Control control in body.Controls)
            control.Dock = DockStyle.Top;

        // Docked children stack in reverse, so the first added must be added last.
        var ordered = body.Controls.Cast<Control>().Reverse().ToArray();
        body.Controls.Clear();
        body.Controls.AddRange(ordered);

        return body;
    }

    private Control BuildRefusalPanel(List<PlannedFile> refused)
    {
        var panel = new Panel { AutoSize = true, Padding = new Padding(0, 12, 0, 0) };

        var explanation = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            ForeColor = Color.FromArgb(150, 40, 0),
            Text =
                $"The source encoding of {refused.Count} file(s) could not be determined "
                + "uniquely. More than one encoding fits the bytes, and they produce "
                + "different text, so converting would pick one reading without saying "
                + "so. No changes will be made to these files.",
        };

        var list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            Height = 120,
            Dock = DockStyle.Top,
        };

        list.Columns.Add("File", 200);
        list.Columns.Add("Detected", 110);
        list.Columns.Add("Also fits, reading it differently", 300);

        foreach (PlannedFile file in refused.Take(200))
        {
            list.Items.Add(new ListViewItem(
            [
                file.RelativePath,
                file.SourceEncoding,
                string.Join(", ", file.CompetingEncodings.Take(6))
                + (file.CompetingEncodings.Count > 6
                    ? $", and {file.CompetingEncodings.Count - 6} more"
                    : string.Empty),
            ]));
        }

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
            Text = "If you know which encoding these files are, choose it:",
        });

        _sourceChoice.DropDownStyle = ComboBoxStyle.DropDownList;
        _sourceChoice.Width = 160;
        _sourceChoice.Items.Add("(leave them alone)");

        // The same set the classifier drew its candidates from, so anything it named as
        // a competing reading can be chosen here.
        foreach (string charset in TextEncoding.SupportedCharsets)
            _sourceChoice.Items.Add(charset);

        _sourceChoice.SelectedIndex = 0;
        _sourceChoice.SelectedIndexChanged += (_, _) =>
            _resolve.Enabled = _sourceChoice.SelectedIndex > 0;

        _resolve.Text = "Use this encoding";
        _resolve.AutoSize = true;
        _resolve.Enabled = false;
        _resolve.Click += (_, _) =>
        {
            ChosenSourceEncoding = (string)_sourceChoice.SelectedItem!;
            DialogResult = DialogResult.Retry;
            Close();
        };

        chooser.Controls.Add(_sourceChoice);
        chooser.Controls.Add(_resolve);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            ForeColor = SystemColors.GrayText,
            Dock = DockStyle.Top,
            Text =
                "Choosing an encoding replaces detection for these files and nothing "
                + "else: the bytes must still decode strictly as it, the result is still "
                + "verified to hold exactly the same text, and a failed backup still "
                + "stops the conversion.",
        };

        panel.Controls.Add(note);
        panel.Controls.Add(chooser);
        panel.Controls.Add(list);
        panel.Controls.Add(explanation);

        explanation.Dock = DockStyle.Top;

        return panel;
    }

    private Control BuildButtons()
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
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
        };

        int convert = Count(PlannedAction.Convert);

        var proceed = new Button
        {
            Text = convert > 0 ? $"Convert {convert} file(s)" : "Nothing to convert",
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
        MaximumSize = new Size(620, 0),
        Padding = new Padding(0, 0, 0, 8),
        Text = text,
    };

    private static Control Rule() => new Label
    {
        BorderStyle = BorderStyle.Fixed3D,
        Height = 2,
        Margin = new Padding(0, 8, 0, 8),
    };

    private static Control Rows(IReadOnlyList<(string Label, int Count, string Note)> rows)
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = rows.Count,
            AutoSize = true,
        };

        foreach ((string label, int count, string note) in rows)
        {
            // Zero-count categories are dropped: a list of noughts buries the lines that
            // actually say something.
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

    private static Control Rows(IReadOnlyList<(string Label, string Value)> rows)
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
