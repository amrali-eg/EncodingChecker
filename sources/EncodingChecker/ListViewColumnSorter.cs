using System;
using System.Collections;
using System.Windows.Forms;

namespace EncodingChecker
{
    public class ListViewColumnSorter : IComparer
    {
        private readonly CaseInsensitiveComparer _objectCompare = new();

        public int SortColumn { get; set; }

        public SortOrder Order { get; set; }

        public int Compare(object? x, object? y)
        {
            if (x is not ListViewItem item1)
                throw new ArgumentNullException(nameof(x));

            if (y is not ListViewItem item2)
                throw new ArgumentNullException(nameof(y));

            int result = _objectCompare.Compare(
                item1.SubItems[SortColumn].Text,
                item2.SubItems[SortColumn].Text);

            // Files are added in non-deterministic (parallel-scan) completion order, so
            // ties on the sort column need a stable tiebreaker across the other columns -
            // otherwise equal rows reshuffle between runs even though the sort is applied.
            if (result == 0)
                result = CompareRemainingColumns(item1, item2);

            return Order switch
            {
                SortOrder.Ascending => result,
                SortOrder.Descending => -result,
                _ => 0,
            };
        }

        private static int CompareRemainingColumns(ListViewItem x, ListViewItem y)
        {
            int count = Math.Min(x.SubItems.Count, y.SubItems.Count);

            for (int i = 0; i < count; i++)
            {
                int result = string.CompareOrdinal(x.SubItems[i].Text, y.SubItems[i].Text);
                if (result != 0)
                    return result;
            }

            return 0;
        }
    }
}
