using Arius.Facade;
using Arius.Models;
using Arius.UI.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Arius.UI.ViewModels
{
    internal class FolderViewModel : ViewModelBase, IEqualityComparer<FolderViewModel>
    {
        public FolderViewModel(FolderViewModel parent)
        {
            this.parent = parent;
        }
        private readonly FolderViewModel parent;

        public string Path
        {
            get
            {
                if (parent is null)
                    return ".";
                else
                {
                    if (parent.Name == ".")
                        return Name;
                    else
                        return $"{parent.Path}{System.IO.Path.DirectorySeparatorChar}{Name}";
                }
            }
        }

        public string Name { get; init; }

        public bool IsSelected { get; set; }
        public bool IsExpanded { get; set; }

        public ObservableCollection<FolderViewModel> Folders { get; init; } = new();

        public ICollection<ItemViewModel> Items => items.Values;
        private readonly Dictionary<string, ItemViewModel> items = new();

        public void Add(IAriusEntry item)
        {
            if (item.RelativePath.Equals(Path))
            {
                // Add to self
                lock (items)
                {
                    if (!items.ContainsKey(item.ContentName))
                        items.Add(item.ContentName, new() { ContentName = item.ContentName });
                }

                if (item is BinaryFile bf)
                    items[item.ContentName].BinaryFile = bf;
                else if (item is PointerFile pf)
                    items[item.ContentName].PointerFile = pf;
                else if (item is PointerFileEntryAriusEntry k)
                    items[item.ContentName].PointerFileEntry = k;
                else
                    throw new NotImplementedException();

                OnPropertyChanged(nameof(Items));
            }
            else
            {
                // Add to child
                var dir = System.IO.Path.GetRelativePath(Path, item.RelativePath);
                dir = dir.Split(System.IO.Path.DirectorySeparatorChar)[0];

                // ensure the child exists
                if (Folders.SingleOrDefault(c => c.Name == dir) is var folder && folder is null)
                {
                    folder = new FolderViewModel(this) { Name = dir };
                    Folders.AddSorted(folder, new FolderViewModelNameComparer());
                }

                folder.Add(item);
            }
        }

        public void Clear()
        {
            items.Clear();
            OnPropertyChanged(nameof(Items));
        }

        public bool Equals(FolderViewModel x, FolderViewModel y)
        {
            return x.Path.Equals(y.Path);
        }

        public int GetHashCode([DisallowNull] FolderViewModel obj)
        {
            return HashCode.Combine(obj.Path);
        }
    }
}
