using System;
using System.Collections;
using System.Windows.Forms;

namespace EncodingChecker
{
    public class ListViewColumnSorter : IComparer
    {
        private readonly CaseInsensitiveComparer _objectCompare;

        public int SortColumn { get; set; }

        public SortOrder Order { get; set; }

        public ListViewColumnSorter()
        {
            SortColumn = 0;
            Order = SortOrder.None;
            _objectCompare = new CaseInsensitiveComparer();
        }

        public int Compare(object x, object y)
        {
            ListViewItem listViewItem = (ListViewItem)x;
            if (listViewItem == null) throw new ArgumentNullException(nameof(listViewItem));

            ListViewItem listViewItem2 = (ListViewItem)y;
            if (listViewItem2 == null) throw new ArgumentNullException(nameof(listViewItem2));

            int compareResult = _objectCompare.Compare(a: listViewItem.SubItems[index: SortColumn].Text, b: listViewItem2.SubItems[index: SortColumn].Text);
            if (Order == SortOrder.Ascending)
            {
                return compareResult;
            }
            if (Order == SortOrder.Descending)
            {
                return -compareResult;
            }
            return 0;
        }
    }
}
