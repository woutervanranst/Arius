using Arius.Models;
using Arius.Repositories;
using Arius.UI.Properties;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

                LoadContainers();
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

                LoadContainers();
            }
        }
        private string storageAccountKey;

        public ObservableCollection<ContainerViewModel> Containers { get; private set; }

        private async void LoadContainers()
        {
            await Task.Run(() =>
            {
                if (string.IsNullOrEmpty(AccountName) || string.IsNullOrEmpty(AccountKey))
                    return;

                try
                {
                    Containers = new(facade.GetAzureRepositoryContainerNames(AccountName, AccountKey).Select(containerName => new ContainerViewModel(AccountName, AccountKey, containerName)));
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.Message, App.Name, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }

                OnPropertyChanged(nameof(Containers));

                SelectedContainer = Containers.First();
                OnPropertyChanged(nameof(SelectedContainer));
            });
        }

        public ContainerViewModel SelectedContainer { get; set; }


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
            if (Folders.SingleOrDefault(tvi => tvi.Name == ".") is var root && root is null)
                Folders.Add(root = new FolderTreeViewItem(null) { Name = ".", IsSelected = true, IsExpanded = true });

            Task.Run(async () =>
            {
                var di = new DirectoryInfo(path);

                await foreach (var item in facade.GetLocalPathItems(di))
                    root.Add(item);
            });
        }

        public ObservableCollection<FolderTreeViewItem> Folders { get; init; } = new();

        public void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            Items = new ObservableCollection<Clip>((e.NewValue as FolderTreeViewItem).Items);
            OnPropertyChanged(nameof(Items));
        }

        public ObservableCollection<Clip> Items { get; set; }

        public class FolderTreeViewItem : ViewModelBase, IEquatable<FolderTreeViewItem>
        {
            public FolderTreeViewItem(FolderTreeViewItem parent)
            {
                this.parent = parent;
            }
            private readonly FolderTreeViewItem parent;

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
                            return parent.Path + System.IO.Path.DirectorySeparatorChar + Name;
                    }
                }
            }

            public string Name { get; init; }

            public bool IsSelected { get; set; }
            public bool IsExpanded { get; set; }

            public ObservableCollection<FolderTreeViewItem> Folders { get; init; } = new();

            //public ICollection<IAriusArchiveItem> Items { get; init; } = new ObservableCollection<IAriusArchiveItem>();
            public ICollection<Clip> Items => items.Values;
            private readonly Dictionary<string, Clip> items = new();

            public void Add(IAriusEntry item)
            {
                if (item.RelativePath.Equals(this.Path))
                {
                    // Add to self
                    //Items.Add(item);

                    lock (items)
                    { 
                        if (!items.ContainsKey(item.Name))
                            items.Add(item.Name, new() { Name = item.Name });
                    }

                    if (item is BinaryFile bf)
                        items[item.Name].BinaryFile = bf;
                    else if (item is PointerFile pf)
                        items[item.Name].PointerFile = pf;
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
                            Folders.Add(folder = new FolderTreeViewItem(this) { Name = dir });

                        folder.Add(item);
                    });
                }
            }

            public bool Equals(FolderTreeViewItem other)
            {
                return other.Name == Name;
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as FolderTreeViewItem);
            }

            public override int GetHashCode()
            {
                return base.GetHashCode();
                //return HashCode.Combine(this, Name);
            }
        }

        public class Clip
        {
            public string Name { get; init; }
            public IAriusEntry PointerFile { get; set; }
            public IAriusEntry BinaryFile { get; set; }

        }
    }

    public class ContainerViewModel
    {
        public ContainerViewModel(string accountName, string accountKey, string containerName)
        {
            Name = containerName;
        }
        public string Name { get; init; }
    }
}
