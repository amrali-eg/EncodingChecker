using System.Windows.Forms;

namespace EncodingChecker.Tests;

/// <summary>
/// ListViewColumnSorter.Compare is pure logic over ListViewItem/SubItems text - no real
/// ListView or Form is needed to construct and compare items standalone.
/// </summary>
public sealed class ListViewColumnSorterTests
{
    private static ListViewItem Row(params string[] columns) => new(columns);

    [Fact]
    public void Ascending_OrdersLowerValueFirst()
    {
        var sorter = new ListViewColumnSorter { SortColumn = 0, Order = SortOrder.Ascending };

        ListViewItem a = Row("apple");
        ListViewItem b = Row("banana");

        Assert.True(sorter.Compare(a, b) < 0);
        Assert.True(sorter.Compare(b, a) > 0);
    }

    [Fact]
    public void Descending_InvertsAscendingResult()
    {
        var ascending = new ListViewColumnSorter { SortColumn = 0, Order = SortOrder.Ascending };
        var descending = new ListViewColumnSorter { SortColumn = 0, Order = SortOrder.Descending };

        ListViewItem a = Row("apple");
        ListViewItem b = Row("banana");

        int ascendingResult = ascending.Compare(a, b);
        int descendingResult = descending.Compare(a, b);

        Assert.True(ascendingResult < 0);
        Assert.True(descendingResult > 0);
        Assert.Equal(-ascendingResult, descendingResult);
    }

    [Fact]
    public void TieOnSortColumn_BreaksOnFirstDifferingSubsequentColumn()
    {
        var sorter = new ListViewColumnSorter { SortColumn = 0, Order = SortOrder.Ascending };

        // Column 0 ties ("same"); column 1 differs and must decide the order.
        ListViewItem x = Row("same", "aaa");
        ListViewItem y = Row("same", "bbb");

        Assert.True(sorter.Compare(x, y) < 0);
        Assert.True(sorter.Compare(y, x) > 0);
    }

    [Fact]
    public void AllColumnsEqual_ReturnsZero()
    {
        var sorter = new ListViewColumnSorter { SortColumn = 0, Order = SortOrder.Ascending };

        ListViewItem x = Row("same", "also-same", "still-same");
        ListViewItem y = Row("same", "also-same", "still-same");

        Assert.Equal(0, sorter.Compare(x, y));
    }

    [Fact]
    public void PrimaryColumnCaseOnlyDifference_IsTreatedAsATie()
    {
        // The primary comparison is case-insensitive, so "Apple"/"apple" tie on column 0.
        // The tiebreak must skip column 0 (already compared), not redundantly re-compare
        // it ordinally - otherwise a case-only difference would decide the order.
        var sorter = new ListViewColumnSorter { SortColumn = 0, Order = SortOrder.Ascending };

        ListViewItem x = Row("Apple", "same");
        ListViewItem y = Row("apple", "same");

        Assert.Equal(0, sorter.Compare(x, y));
    }

    [Fact]
    public void PrimaryColumnCaseOnlyDifference_FallsThroughToRealDifferenceInLaterColumn()
    {
        var sorter = new ListViewColumnSorter { SortColumn = 0, Order = SortOrder.Ascending };

        // Column 0 ties (case-insensitive); column 1 genuinely differs and must decide.
        ListViewItem x = Row("Apple", "aaa");
        ListViewItem y = Row("apple", "zzz");

        Assert.True(sorter.Compare(x, y) < 0);
        Assert.True(sorter.Compare(y, x) > 0);
    }

    [Fact]
    public void TiebreakSkipsSortColumnWhenNotFirst()
    {
        // Same case-only-tie check with SortColumn = 1, to confirm the tiebreak skips
        // whichever column was already compared, not always index 0.
        var sorter = new ListViewColumnSorter { SortColumn = 1, Order = SortOrder.Ascending };

        ListViewItem x = Row("same", "Apple");
        ListViewItem y = Row("same", "apple");

        Assert.Equal(0, sorter.Compare(x, y));
    }

    [Fact]
    public void NonListViewItemArguments_Throw()
    {
        var sorter = new ListViewColumnSorter { SortColumn = 0, Order = SortOrder.Ascending };

        Assert.Throws<ArgumentNullException>(() => sorter.Compare("not a ListViewItem", Row("x")));
        Assert.Throws<ArgumentNullException>(() => sorter.Compare(Row("x"), "not a ListViewItem"));
    }
}
