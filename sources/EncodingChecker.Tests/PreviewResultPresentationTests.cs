using System.Windows.Forms;

namespace EncodingChecker.Tests;

/// <summary>
/// MainForm.UpdateResultItem is the single place a result row is formatted, and it is
/// pure logic over a ListViewItem - no real form or message loop needed, the same way
/// ListViewColumnSorterTests exercises the sorter.
///
/// These cover the presentation contract only. The engine-level guarantees (preview
/// writes nothing, creates no .bak) are already proven by
/// ConvertFilesPreviewAndBackupTests and are deliberately not duplicated here.
/// </summary>
public sealed class PreviewResultPresentationTests
{
    private const int CharsetColumn = 0;

    // Positions in imgsResults; see the SetKeyName calls in MainForm.Designer.cs.
    // SetKeyName runs in InitializeComponent, not in the resource stream, so a
    // form-less test can only address these images by index.
    private const int SuccessIcon = 0;
    private const int FailedIcon = 1;

    private static ListViewItem Row(string charset) =>
        new([charset, "f.txt", ".txt", @"C:\dir"]);

    private static ConversionReportEntry Entry(ConversionRowResult result) => new()
    {
        FilePath = @"C:\dir\f.txt",
        SourceEncoding = "us-ascii",
        SourceHasBom = false,
        TargetEncoding = "utf-8",
        TargetHasBom = true,
        Result = result,
    };

    [Fact]
    public void Preview_WouldConvert_KeepsRowChecked_KeepsCharset_AndUsesTheWouldChangeIcon()
    {
        ListViewItem item = Row("us-ascii");
        item.Checked = true;

        MainForm.UpdateResultItem(
            item,
            Entry(ConversionRowResult.Converted),
            targetLabel: "utf-8-bom",
            wasPreview: true);

        // The file was never written, so the row must still describe it as it is on
        // disk - reporting the target encoding here is what made preview look like a
        // completed conversion.
        Assert.Equal("us-ascii", item.SubItems[CharsetColumn].Text);

        // Preview must not destroy the user's selection: the whole point is to inspect
        // the result and then run a real Convert on the still-checked rows.
        Assert.True(item.Checked);

        // Distinct from the success icon so "would convert" can't be mistaken for done.
        Assert.NotEqual(0, item.ImageIndex);
    }

    [Fact]
    public void RealConvert_Converted_StillUnchecksAndShowsTheTargetCharsetAndSuccessIcon()
    {
        ListViewItem item = Row("us-ascii");
        item.Checked = true;

        MainForm.UpdateResultItem(
            item,
            Entry(ConversionRowResult.Converted),
            targetLabel: "utf-8-bom",
            wasPreview: false);

        // Unchanged pre-existing behaviour for an actual conversion.
        Assert.Equal("utf-8-bom", item.SubItems[CharsetColumn].Text);
        Assert.False(item.Checked);
        Assert.Equal(SuccessIcon, item.ImageIndex);
    }

    [Fact]
    public void Preview_And_RealConvert_AreVisuallyDistinguishable()
    {
        ListViewItem previewed = Row("us-ascii");
        ListViewItem converted = Row("us-ascii");

        MainForm.UpdateResultItem(previewed, Entry(ConversionRowResult.Converted), "utf-8-bom", wasPreview: true);
        MainForm.UpdateResultItem(converted, Entry(ConversionRowResult.Converted), "utf-8-bom", wasPreview: false);

        Assert.NotEqual(converted.ImageIndex, previewed.ImageIndex);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Error_UsesTheFailedIcon_KeepsCharset_AndStaysChecked(bool wasPreview)
    {
        ListViewItem item = Row("us-ascii");
        item.Checked = true;

        MainForm.UpdateResultItem(
            item,
            Entry(ConversionRowResult.Error),
            targetLabel: "utf-8-bom",
            wasPreview: wasPreview);

        // Nothing was written, so the row keeps its charset and stays selected for a
        // retry; only the icon marks the failure.
        Assert.Equal("us-ascii", item.SubItems[CharsetColumn].Text);
        Assert.True(item.Checked);
        Assert.Equal(FailedIcon, item.ImageIndex);
    }

    [Fact]
    public void Error_And_PreviewWouldChange_AreVisuallyDistinguishable()
    {
        ListViewItem failed = Row("us-ascii");
        ListViewItem previewed = Row("us-ascii");
        ListViewItem converted = Row("us-ascii");

        MainForm.UpdateResultItem(failed, Entry(ConversionRowResult.Error), "utf-8-bom", wasPreview: false);
        MainForm.UpdateResultItem(previewed, Entry(ConversionRowResult.Converted), "utf-8-bom", wasPreview: true);
        MainForm.UpdateResultItem(converted, Entry(ConversionRowResult.Converted), "utf-8-bom", wasPreview: false);

        // All three outcomes a Convert run can produce must be tellable apart at a glance.
        Assert.Equal(3, new[] { failed.ImageIndex, previewed.ImageIndex, converted.ImageIndex }.Distinct().Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnchangedAndSkipped_LeaveTheRowAlone(bool wasPreview)
    {
        foreach (ConversionRowResult result in
                 new[] { ConversionRowResult.Unchanged, ConversionRowResult.Skipped })
        {
            ListViewItem item = Row("us-ascii");
            item.Checked = true;

            MainForm.UpdateResultItem(item, Entry(result), "utf-8-bom", wasPreview);

            Assert.Equal("us-ascii", item.SubItems[CharsetColumn].Text);
            Assert.True(item.Checked);
        }
    }

    [Fact]
    public void TheWouldChangeIconIndexActuallyExistsInTheResultsImageList()
    {
        // UpdateResultItem indexes imgsResults by position, so a designer regeneration
        // that dropped an image would silently produce a blank/out-of-range icon.
        var resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));

        using var imageList = new ImageList();
        imageList.ImageStream = (ImageListStreamer?)resources.GetObject("imgsResults.ImageStream");

        // Success (0), Failed (1), Warning/would-change (2).
        Assert.True(
            imageList.Images.Count >= 3,
            $"imgsResults must contain at least 3 images; found {imageList.Images.Count}.");
    }
}
