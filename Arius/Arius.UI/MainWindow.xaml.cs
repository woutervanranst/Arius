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

    public class MainViewModel : INotifyPropertyChanged
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
                    Items.Add(item);

                    RootItem[0].AddFolderItem(item);

                    //AddFolderItem(item.RelativePath, item);

                    //var tvi = new TreeViewItem { Name = item.RelativePath };



                    //if (!Folders.Contains(tvi))
                    //{
                    //    Folders.Add(tvi);
                    //}

                }

                //App.Current.Dispatcher.Invoke(() =>
                //{
                //    Folders.AddRange(xx);
                //});

                //Folders = new ObservableCollection<TreeViewItem>(Directory.GetDirectories(path).Select(d => new TreeViewItem { Name = d }));

                OnPropertyChanged(nameof(RootItem));
            });
        }

        public List<TreeViewItem> RootItem { get; init; } = new List<TreeViewItem> { new TreeViewItem(null) { Name = "." } };
        private List<IAriusArchiveItem> Items { get; init; } = new();

        //public ICollection<TreeViewItem> Folders => folders.Values;
        //private readonly Dictionary<string, TreeViewItem> folders = new();

        public class TreeViewItem : IEquatable<TreeViewItem>
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
                        return parent.Name + System.IO.Path.DirectorySeparatorChar + Name;
                }
            }

            public string Name { get; init; }

            public ICollection<TreeViewItem> Children => children.Values;
            private readonly Dictionary<string, TreeViewItem> children = new();

            public void AddFolderItem(IAriusArchiveItem item)
            {
                if (item.RelativePath.Equals(this.Path))
                {
                    // Add to self
                    children.Add(item.RelativeName, item);
                }
                else
                {
                    // Add to child

                    // ensure the child exists

                    
                }


                var dir = item.RelativePath.Split(System.IO.Path.DirectorySeparatorChar)[0];

                if (!children.ContainsKey(dir))
                {

                }



            }

            public bool Equals(TreeViewItem other)
            {
                return other.Name == Name;
            }

        }
        





        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
