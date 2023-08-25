using System.Collections.ObjectModel;

namespace Arius.UI.Utils;

public class SortedObservableCollection<T> : ObservableCollection<T>
{
    private readonly IComparer<T> _comparer;

    public SortedObservableCollection(IComparer<T> comparer)
    {
        _comparer = comparer ?? Comparer<T>.Default;
    }

    public SortedObservableCollection(IEnumerable<T> collection, IComparer<T> comparer)
        : base(collection)
    {
        _comparer = comparer ?? Comparer<T>.Default;
        Sort();
    }

    protected override void InsertItem(int index, T item)
    {
        for (int i = 0; i < Count; i++)
        {
            switch (Math.Sign(_comparer.Compare(item, Items[i])))
            {
                case 0:
                case -1:
                    base.InsertItem(i, item);
                    return;
            }
        }

        base.InsertItem(Count, item);
    }

    protected void Sort()
    {
        var sorted = this.OrderBy(x => x, _comparer).ToList();
        for (int i = 0; i < sorted.Count(); i++)
        {
            Move(IndexOf(sorted[i]), i);
        }
    }
}