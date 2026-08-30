using System.Reflection;
using System.Windows.Forms;

namespace EncodingChecker.Tests;

/// <summary>
/// The CSV report describes whatever is on screen. Conversion history describes only
/// the current conversion run. Keep those availability rules visible and separate.
/// </summary>
public sealed class ExportMenuTests
{
    [Fact]
    public void NewWindowDefaultsToUtf8WithoutABom()
    {
        UiTest.OnStaThread(() =>
        {
            using var form = new MainForm();
            typeof(MainForm)
                .GetMethod("OnFormLoad", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(form, [form, EventArgs.Empty]);
            var target = (ComboBox)typeof(MainForm)
                .GetField("lstConvert", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(form)!;

            Assert.Equal("utf-8", target.SelectedItem);
        });
    }

    [Fact]
    public void MainEncodingSelectorsUseTheSharedRuntimeSupportedList()
    {
        UiTest.OnStaThread(() =>
        {
            using var form = new MainForm();
            typeof(MainForm)
                .GetMethod("OnFormLoad", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(form, [form, EventArgs.Empty]);

            var valid = (CheckedListBox)typeof(MainForm)
                .GetField("lstValidCharsets", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(form)!;
            var target = (ComboBox)typeof(MainForm)
                .GetField("lstConvert", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(form)!;

            string[] expected =
            [
                .. TextEncoding.SupportedEncodings.SelectMany(encoding =>
                    ScanEngine.IsBomCapable(encoding.WebName)
                        ? new[] { encoding.WebName, encoding.WebName + "-bom" }
                        : new[] { encoding.WebName })
            ];

            Assert.Equal(expected, valid.Items.Cast<string>());
            Assert.Equal(expected, target.Items.Cast<string>());
        });
    }

    [Fact]
    public void CsvAndConversionHistoryHaveDistinctAvailabilityRules()
    {
        UiTest.OnStaThread(() =>
        {
            using var form = new MainForm();
            ToolStripDropDownButton export = FindExportButton(form);
            Assert.Equal(3, export.DropDownItems.Count);
            ToolStripMenuItem text = Assert.IsType<ToolStripMenuItem>(export.DropDownItems[0]);
            ToolStripMenuItem csv = Assert.IsType<ToolStripMenuItem>(export.DropDownItems[1]);
            ToolStripMenuItem history = Assert.IsType<ToolStripMenuItem>(export.DropDownItems[2]);

            Assert.Equal("Export selected rows as text...", text.Text);
            Assert.Equal("Export all results as CSV...", csv.Text);
            Assert.Equal("Export conversion journal as JSON...", history.Text);

            // A View/Validate result set can be exported as CSV, but it is not a
            // conversion history.
            ListView results = Assert.Single(form.Controls.OfType<ListView>());
            results.CheckBoxes = true;
            results.Items.Add(new ListViewItem("utf-8") { Checked = true });
            RefreshMenu(form, export);

            Assert.True(text.Enabled);
            Assert.True(csv.Enabled);
            Assert.False(history.Enabled);

            // A completed conversion enables its immutable JSON journal.
            SetLastConversion(form, Journal());
            RefreshMenu(form, export);

            Assert.True(csv.Enabled);
            Assert.True(history.Enabled);

            // Starting a new View/Validate scan clears the conversion state. The CSV
            // still represents displayed results; history must not describe this scan.
            SetLastConversion(form, null);
            RefreshMenu(form, export);

            Assert.True(csv.Enabled);
            Assert.False(history.Enabled);
        });
    }

    private static ToolStripDropDownButton FindExportButton(MainForm form) =>
        Assert.Single(
            Assert.Single(form.Controls.OfType<StatusStrip>()).Items
                .OfType<ToolStripDropDownButton>(),
            item => item.Name == "btnExportReport");

    private static void RefreshMenu(MainForm form, ToolStripDropDownButton export) =>
        typeof(MainForm)
            .GetMethod("OnExportResultsOpening", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, [export, EventArgs.Empty]);

    private static void SetLastConversion(MainForm form, ConversionJournal? value) =>
        typeof(MainForm)
            .GetField("_lastConversionJournal", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(form, value);

    private static ConversionJournal Journal() => new()
    {
        EcVersion = "test",
        StartedUtc = DateTime.UtcNow.ToString("O"),
        CompletedUtc = DateTime.UtcNow.ToString("O"),
        Surface = "Gui",
        BaseDirectory = Path.GetTempPath(),
        TargetEncoding = "utf-8",
        TargetHasBom = false,
        BackupEnabled = true,
        Entries = [],
    };

}
