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
    public void PrimaryColumnCaseOnlyDifference_IsNotTreatedAsATie()
    {
        // Documents a real, verified quirk (found while writing this test, not a
        // deliberate design choice - see the code review flag raised alongside this
        // suite): the primary-column comparison itself IS case-insensitive
        // (CaseInsensitiveComparer judges "Apple"/"apple" equal), but
        // CompareRemainingColumns' tiebreak loop starts at index 0 instead of the
        // column after SortColumn. It redundantly re-compares the primary column
        // too, this time via string.CompareOrdinal (case-sensitive), so a
        // primary-column difference that's only a case difference is NOT actually
        // treated as a tie end-to-end - it resolves ordinally instead of falling
        // through to genuinely compare the next column.
        var sorter = new ListViewColumnSorter { SortColumn = 0, Order = SortOrder.Ascending };

        ListViewItem x = Row("Apple", "same");
        ListViewItem y = Row("apple", "same");

        int result = sorter.Compare(x, y);

        Assert.NotEqual(0, result);
        Assert.Equal(string.CompareOrdinal("Apple", "apple") < 0, result < 0);
    }

    [Fact]
    public void PrimaryColumnCaseOnlyDifference_WinsOverARealDifferenceInALaterColumn()
    {
        // Same quirk, made concrete: column 1 genuinely differs ("aaa" vs "zzz"), but
        // the tiebreak's redundant re-check of column 0 fires first and decides the
        // result, so the later column's real difference is never even reached.
        var sorter = new ListViewColumnSorter { SortColumn = 0, Order = SortOrder.Ascending };

        ListViewItem x = Row("Apple", "zzz");
        ListViewItem y = Row("apple", "aaa");

        int result = sorter.Compare(x, y);

        Assert.Equal(string.CompareOrdinal("Apple", "apple") < 0, result < 0);
    }

    [Fact]
    public void NonListViewItemArguments_Throw()
    {
        var sorter = new ListViewColumnSorter { SortColumn = 0, Order = SortOrder.Ascending };

        Assert.Throws<ArgumentNullException>(() => sorter.Compare("not a ListViewItem", Row("x")));
        Assert.Throws<ArgumentNullException>(() => sorter.Compare(Row("x"), "not a ListViewItem"));
    }
}
