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
    public void CsvAndConversionHistoryHaveDistinctAvailabilityRules()
    {
        UiTest.OnStaThread(() =>
        {
            using var form = new MainForm();
            ToolStripDropDownButton export = FindExportButton(form);
            ToolStripMenuItem csv = Assert.IsType<ToolStripMenuItem>(export.DropDownItems[0]);
            ToolStripMenuItem history = Assert.IsType<ToolStripMenuItem>(export.DropDownItems[1]);

            // A View/Validate result set can be exported as CSV, but it is not a
            // conversion history.
            ListView results = Assert.Single(form.Controls.OfType<ListView>());
            results.Items.Add(new ListViewItem("utf-8"));
            RefreshMenu(form, export);

            Assert.True(csv.Enabled);
            Assert.False(history.Enabled);

            // A completed conversion enables the JSON history for these same results.
            SetLastConversion(form, DateTime.UtcNow);
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

    private static void SetLastConversion(MainForm form, DateTime? value) =>
        typeof(MainForm)
            .GetField("_lastConversionStartedUtc", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(form, value);

}
