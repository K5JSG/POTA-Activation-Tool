using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace PotaActivationTool
{
    // A regular List<T> bound to a DataGridView will throw if you click a column
    // header to sort it - plain lists don't know how to sort themselves. This
    // class wraps a list and adds that missing sorting behavior, so clicking any
    // column header in the grid sorts by that column (click again to reverse).
    public class SortableBindingList<T> : BindingList<T>
    {
        private bool _isSorted;
        private ListSortDirection _sortDirection = ListSortDirection.Ascending;
        private PropertyDescriptor? _sortProperty;

        public SortableBindingList(IEnumerable<T> items) : base(new List<T>(items))
        {
        }

        protected override bool SupportsSortingCore => true;
        protected override bool IsSortedCore => _isSorted;
        protected override ListSortDirection SortDirectionCore => _sortDirection;
        protected override PropertyDescriptor? SortPropertyCore => _sortProperty;

        protected override void ApplySortCore(PropertyDescriptor property, ListSortDirection direction)
        {
            var items = (List<T>)Items;

            Comparison<T> comparer = (a, b) =>
            {
                object? valueA = property.GetValue(a);
                object? valueB = property.GetValue(b);
                int result = CompareValues(valueA, valueB);
                return direction == ListSortDirection.Ascending ? result : -result;
            };

            items.Sort(comparer);

            _sortProperty = property;
            _sortDirection = direction;
            _isSorted = true;

            OnListChanged(new ListChangedEventArgs(ListChangedType.Reset, -1));
        }

        protected override void RemoveSortCore()
        {
            _isSorted = false;
            _sortProperty = null;
        }

        private static int CompareValues(object? a, object? b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            if (a is IComparable comparableA)
                return comparableA.CompareTo(b);

            return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
