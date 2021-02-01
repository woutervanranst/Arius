using Arius.Models;
using Arius.Repositories;
using Arius.UI.Properties;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

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

            //this.DataContext = new MainViewModel();
        }

        //public IEnumerable<string> ha
        //{
        //    get
        //    {
        //        return new List<string>() { "haha", "hehe" };
        //    }
        //}
    }

    internal static class StringExtensions
    {
        public static string Protect(this string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            byte[] entropy = Encoding.UTF8.GetBytes(Assembly.GetExecutingAssembly().FullName);
            byte[] data = Encoding.UTF8.GetBytes(value);
            string protectedData = Convert.ToBase64String(ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser));
            return protectedData;
        }

        public static string Unprotect(this string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            byte[] protectedData = Convert.FromBase64String(value);
            byte[] entropy = Encoding.UTF8.GetBytes(Assembly.GetExecutingAssembly().FullName);
            string data = Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedData, entropy, DataProtectionScope.CurrentUser));
            return data;
        }
    }

    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        //[NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

        public IEnumerable<ContainerViewModel> Containers { get; private set; }

        private void LoadContainers()
        {
            Task.Run(() =>
            {
                if (string.IsNullOrEmpty(AccountName) || string.IsNullOrEmpty(AccountKey))
                    return;

                try
                {
                    Containers = facade.GetAzureRepositoryContainerNames(AccountName, AccountKey).Select(containerName => new ContainerViewModel(AccountName, AccountKey, containerName));
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.Message, App.Name, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }

                SelectedContainer = Containers.First();
                OnPropertyChanged(nameof(SelectedContainer));
                OnPropertyChanged(nameof(Containers));
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
            Task.Run(async () =>
            {
                await Task.Yield();

                //await Task.Delay(500);

                var di = new DirectoryInfo(path);

                await foreach (var item in facade.GetLocalPathItems(di))
                {
                    RootItem[0].AddFolderItem(item);
                }

                //App.Current.Dispatcher.Invoke(() =>
                //{
                //    Folders.AddRange(xx);
                //});

                //Folders = new ObservableCollection<TreeViewItem>(Directory.GetDirectories(path).Select(d => new TreeViewItem { Name = d }));

                OnPropertyChanged(nameof(RootItem));
            });

            //var z = new TreeViewItem(null) { Name = "he" };
            //z.Children.Add(new TreeViewItem(z) { Name = "ha" });
            //RootItem.Add(z);


            OnPropertyChanged(nameof(RootItem));

        }

        public List<TreeViewItem> RootItem { get; init; } = new List<TreeViewItem> { new TreeViewItem(null) { Name = "." } };
        //private List<IAriusArchiveItem> Items { get; init; } = new();

        //public ICollection<TreeViewItem> Folders => folders.Values;
        //private readonly Dictionary<string, TreeViewItem> folders = new();

        public class TreeViewItem : ViewModelBase, IEquatable<TreeViewItem>
        {
            public TreeViewItem(TreeViewItem parent)
            {
                this.parent = parent;
            }
            private readonly TreeViewItem parent;

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

            public ICollection<TreeViewItem> Children => children.Values;
            private readonly Dictionary<string, TreeViewItem> children = new();

            public ICollection<IAriusArchiveItem> Items { get; init; } = new ObservableCollection<IAriusArchiveItem>();

            public void AddFolderItem(IAriusArchiveItem item)
            {
                if (item.RelativePath.Equals(this.Path))
                {
                    // Add to self
                    Items.Add(item);

                }
                else
                {
                    // Add to child
                    //var dir = item.RelativePath.Split(System.IO.Path.DirectorySeparatorChar)[0];
                    var dir = System.IO.Path.GetRelativePath(this.Path, item.RelativePath);
                    dir = dir.Split(System.IO.Path.DirectorySeparatorChar)[0];

                    // ensure the child exists
                    if (!children.ContainsKey(dir))
                        children.Add(dir, new TreeViewItem(this) { Name = dir });

                    children[dir].AddFolderItem(item);

                    OnPropertyChanged(nameof(Children));

                    //children.Add(item.RelativeName, item);
                }
            }

            public bool Equals(TreeViewItem other)
            {
                return other.Name == Name;
            }

        }
        





        //public event PropertyChangedEventHandler PropertyChanged;

        //protected virtual void OnPropertyChanged(string propertyName)
        //{
        //    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        //}
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
