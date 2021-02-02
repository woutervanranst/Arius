using Arius.Extensions;
using Arius.Facade;
using Arius.Models;
using Arius.Repositories;
using Arius.UI.Properties;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Arius.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // sorry...
            ((MainViewModel)this.DataContext).TreeView_SelectedItemChanged(sender, e);
        }
    }
    public class MainViewModel : ViewModelBase
    {
        public MainViewModel(Facade.Facade facade)
        {
            this.facade = facade;

            AccountName = Settings.Default.AccountName;
            AccountKey = Settings.Default.AccountKey.Unprotect();
            Passphrase = Settings.Default.Passphrase.Unprotect();

            LocalPath = Settings.Default.LocalPath;
        }
        private readonly Facade.Facade facade;


        public string AccountName
        {
            get => storageAccountName;
            set
            {
                storageAccountName = value;

                Settings.Default.AccountName = value;
                Settings.Default.Save();

                LoadContainers().ConfigureAwait(false);
            }
        }
        private string storageAccountName;

        public string AccountKey
        {
            get => storageAccountKey;
            set
            {
                storageAccountKey = value;

                Settings.Default.AccountKey = value.Protect();
                Settings.Default.Save();

                LoadContainers().ConfigureAwait(false);
            }
        }
        private string storageAccountKey;

        private async Task LoadContainers()
        {
            await Task.Run(() =>
            {
                if (string.IsNullOrEmpty(AccountName) || string.IsNullOrEmpty(AccountKey))
                    return;

                try
                {
                    saf = facade.GetStorageAccountFacade(AccountName, AccountKey);

                    Containers = new(saf.Containers); //.Select(containerName => new ContainerViewModel(AccountName, AccountKey, containerName)));
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.Message, App.Name, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }

                OnPropertyChanged(nameof(Containers));

                SelectedContainer = Containers.SingleOrDefault(cf => cf.Name == Settings.Default.SelectedContainer) ?? Containers.First();
                OnPropertyChanged(nameof(SelectedContainer));
            });
        }
        private Facade.StorageAccountFacade saf;

        public ObservableCollection<Facade.ContainerFacade> Containers { get; private set; }

        public Facade.ContainerFacade SelectedContainer 
        {
            get => selectedContainer;
            set
            {
                selectedContainer = value;

                Settings.Default.SelectedContainer = value?.Name;
                Settings.Default.Save();

                LoadRemoteEntries().ConfigureAwait(false);
            }
        }
        private Facade.ContainerFacade selectedContainer;

        public string Passphrase
        {
            get => passphrase;
            set
            {
                passphrase = value;

                Settings.Default.Passphrase = value.Protect();
                Settings.Default.Save();

                LoadRemoteEntries().ConfigureAwait(false);
            }
        }
        private string passphrase;

        private async Task LoadRemoteEntries()
        {
            var root = GetRoot();

            await Task.Run(async () =>
            {
                if (SelectedContainer is null || string.IsNullOrEmpty(Passphrase))
                    return;

                arf = SelectedContainer.GetAzureRepositoryFacade(Passphrase);

                await foreach (var item in arf.GetRemoteEntries())
                    root.Add(item);

            });
        }
        private Facade.AzureRepositoryFacade arf;


        public string LocalPath
        {
            get => localPath;
            set
            {
                if (string.IsNullOrEmpty(value))
                    return;

                localPath = value;

                Settings.Default.LocalPath = value;
                Settings.Default.Save();

                LoadFolders(value);
            }
        }
        private string localPath;

        private void LoadFolders(string path)
        {
            var root = GetRoot();

            Task.Run(async () =>
            {
                var di = new DirectoryInfo(path);

                await foreach (var item in facade.GetLocalEntries(di))
                    root.Add(item);
            });
        }

        public ObservableCollection<FolderViewModel> Folders { get; init; } = new();

        private FolderViewModel GetRoot()
        {
            if (Folders.SingleOrDefault(tvi => tvi.Name == ".") is var root && root is null)
                Folders.Add(root = new FolderViewModel(null) { Name = ".", IsSelected = true, IsExpanded = true });

            return root;
        }

        public void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            Items = new ObservableCollection<ItemViewModel>((e.NewValue as FolderViewModel).Items);
            OnPropertyChanged(nameof(Items));
        }

        public ObservableCollection<ItemViewModel> Items { get; set; }

        

        
    }

    public class ContainerViewModel
    {
        public ContainerViewModel(string accountName, string accountKey, string containerName)
        {
            Name = containerName;
        }
        public string Name { get; init; }
    }

    public class FolderViewModel : ViewModelBase, IEqualityComparer<FolderViewModel> //, IEquatable<FolderTreeViewItemViewModel>
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
            if (item.RelativePath.Equals(this.Path))
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
                else if (item is kaka k)
                    items[item.ContentName].PointerFileEntry = k;
                else
                    throw new NotImplementedException();

            }
            else
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    // Add to child
                    var dir = System.IO.Path.GetRelativePath(this.Path, item.RelativePath);
                    dir = dir.Split(System.IO.Path.DirectorySeparatorChar)[0];

                    // ensure the child exists
                    if (Folders.SingleOrDefault(c => c.Name == dir) is var folder && folder is null)
                        Folders.Add(folder = new FolderViewModel(this) { Name = dir });

                    folder.Add(item);
                });
            }
        }

        public bool Equals(FolderViewModel x, FolderViewModel y)
        {
            return x.Path.Equals(y.Path);
        }

        public int GetHashCode([DisallowNull] FolderViewModel obj)
        {
            return HashCode.Combine(obj.Path);
        }

        //public bool Equals(FolderTreeViewItemViewModel other)
        //{
        //    return other.Name == Name;
        //}

        //public override bool Equals(object obj)
        //{
        //    return Equals(obj as FolderTreeViewItemViewModel);
        //}

        //public override int GetHashCode()
        //{
        //    return base.GetHashCode();
        //    //return HashCode.Combine(this, Name);
        //}
    }

    public class ItemViewModel
    {
        public string ContentName { get; init; }
        public PointerFile PointerFile { get; set; }
        public BinaryFile BinaryFile { get; set; }
        public kaka PointerFileEntry { get; set; }

        public string Local
        {
            get
            {
                return BinaryFile is not null ? "Yes": "No";
            }
        }
        public string Pointer
        {
            get
            {
                return PointerFile is not null ? "Yes" : "No";
            }
        }
        public string Remote
        {
            get
            {
                return PointerFileEntry is not null ? "Yes" : "No";
            }
        }
        public string Size
        {
            get
            {
                if (BinaryFile is not null)
                    return BinaryFile.Length.GetBytesReadable(LongExtensions.Size.KB);
                else if (Remote is not null)
                    return "TODO";
                else if (PointerFile is not null)
                    return "Unknown";
                else
                    throw new NotImplementedException();
            }
        }
    }
}
