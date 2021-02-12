using Arius.Extensions;
using Arius.Facade;
using Arius.Models;
using Arius.UI.Properties;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Arius.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
    }

    internal class MainViewModel : ViewModelBase
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


        // --- ACCOUNTNAME & ACCOUNTKEY

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


        // --- CONTAINERS

        private void LoadContainers()
        {
            if (string.IsNullOrEmpty(AccountName) || string.IsNullOrEmpty(AccountKey))
                return;

            try
            {
                saf = facade.GetStorageAccountFacade(AccountName, AccountKey);

                Containers = new(saf.Containers.Select(cf => new ContainerViewModel(cf)));
                OnPropertyChanged(nameof(Containers));
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, App.Name, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            SelectedContainer = Containers.SingleOrDefault(cf => cf.Name == Settings.Default.SelectedContainer) ?? Containers.First();
            OnPropertyChanged(nameof(SelectedContainer));
        }
        private StorageAccountFacade saf;

        public ObservableCollection<ContainerViewModel> Containers { get; private set; }

        public ContainerViewModel SelectedContainer 
        {
            get => selectedContainer;
            set
            {
                selectedContainer = value;

                Settings.Default.SelectedContainer = value?.Name;
                Settings.Default.Save();

                //LoadVersions().ConfigureAwait(false);
                LoadAzureRepositoryFacade().ConfigureAwait(false);
            }
        }
        private ContainerViewModel selectedContainer;


        // -- PASSPHRASE

        public string Passphrase
        {
            get => passphrase;
            set
            {
                passphrase = value;

                Settings.Default.Passphrase = value.Protect();
                Settings.Default.Save();

                //LoadVersions().ConfigureAwait(false);
                LoadAzureRepositoryFacade().ConfigureAwait(false);
            }
        }
        private string passphrase;

        private async Task LoadAzureRepositoryFacade()
        {
            if (SelectedContainer is null || string.IsNullOrEmpty(Passphrase))
                azureRepositoryFacade = null;
            else
                azureRepositoryFacade = SelectedContainer.GetAzureRepositoryFacade(Passphrase);

            if (azureRepositoryFacade is not null)
                await LoadVersions();
        }
        private AzureRepositoryFacade azureRepositoryFacade;


        // -- VERSION

        private async Task LoadVersions()
        {
            LoadingRemote = true;

            Versions = new((await azureRepositoryFacade.GetVersions()).Reverse());
            OnPropertyChanged(nameof(Versions));

            SelectedVersion = Versions.First();

            LoadingRemote = false;
        }
        public ObservableCollection<DateTime> Versions { get; private set; }

        public DateTime SelectedVersion
        {
            get => selectedVersion;
            set
            {
                selectedVersion = value;
                OnPropertyChanged();

                LoadRepositoryEntries().ConfigureAwait(false);
            }
        }
        private DateTime selectedVersion;


        // -- INCLUDEDELETEDITEMS

        public bool IncludeDeletedItems
        {
            get => includeDeletedItems;
            set
            {
                includeDeletedItems = value;

                //TODO REFRESH
            }
        }
        private bool includeDeletedItems = false;

        
        // -- REPOSITORY ENTRIES

        private async Task LoadRepositoryEntries()
        {
            try
            {
                LoadingRemote = true;

                var root = GetRoot();

                await foreach (var item in azureRepositoryFacade.GetRemoteEntries(SelectedVersion, IncludeDeletedItems))
                    root.Add(item);

                //OnPropertyChanged(nameof(Folders));
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
            }
            finally
            {
                LoadingRemote = false;
            }
        }

        public bool LoadingRemote
        {
            get => loadingRemote;
            set
            {
                loadingRemote = value;
                OnPropertyChanged();
            }
        }
        private bool loadingRemote;


        // -- LOCAL PATH + LOCAL ENTRIES

        public string LocalPath
        {
            get => localPath;
            set
            {
                localPath = value;

                Settings.Default.LocalPath = value;
                Settings.Default.Save();

                if (string.IsNullOrEmpty(value))
                    return;

                LoadLocalEntries(value).ConfigureAwait(false);
            }
        }
        private string localPath;

        private async Task LoadLocalEntries(string path)
        {
            LoadingLocal = true;

            var root = GetRoot();

            var di = new DirectoryInfo(path);

            await foreach (var item in facade.GetLocalEntries(di))
                root.Add(item);

            //OnPropertyChanged(nameof(Folders));

            LoadingLocal = false;
        }

        public bool LoadingLocal
        {
            get => loadingLocal;
            set
            {
                loadingLocal = value;
                OnPropertyChanged();
            }
        }
        private bool loadingLocal;

        // -- TREEVIEW

        public ObservableCollection<FolderViewModel> Folders { get; init; } = new();

        private FolderViewModel GetRoot()
        {
            if (Folders.SingleOrDefault(tvi => tvi.Name == ".") is var root && root is null)
                Folders.Add(root = new FolderViewModel(null) { Name = ".", IsSelected = true, IsExpanded = true });

            return root;
        }
    }

    internal class ContainerViewModel : ViewModelBase
    {
        public ContainerViewModel(ContainerFacade cf)
        {
            this.cf = cf ?? throw new ArgumentNullException(nameof(ContainerViewModel.cf));
        }
        private readonly ContainerFacade cf;


        public string Name => cf.Name;

        public AzureRepositoryFacade GetAzureRepositoryFacade(string passphrase)
        {
            return cf.GetAzureRepositoryFacade(passphrase);
        }
    }

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
                else if (item is PointerFileEntryAriusEntry k)
                    items[item.ContentName].PointerFileEntry = k;
                else
                    throw new NotImplementedException();

                OnPropertyChanged(nameof(Items));
            }
            else
            {
                // Add to child
                var dir = System.IO.Path.GetRelativePath(this.Path, item.RelativePath);
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

        public bool Equals(FolderViewModel x, FolderViewModel y)
        {
            return x.Path.Equals(y.Path);
        }

        public int GetHashCode([DisallowNull] FolderViewModel obj)
        {
            return HashCode.Combine(obj.Path);
        }
    }

    internal class FolderViewModelNameComparer : IComparer<FolderViewModel>
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int StrCmpLogicalW(string x, string y);

        public int Compare(FolderViewModel x, FolderViewModel y)
        {
            //return x.Name.CompareTo(y.Name);

            // Compare like how Windows Explorer compares - ie Season 1, Season 2, ... Season 10 (not Season 1, Season 10, ...)
            return StrCmpLogicalW(x.Name, y.Name);
        }
    }

    internal class ItemViewModel : ViewModelBase
    {

        public string ContentName { get; init; }
        public PointerFile PointerFile
        {
            get => pointerFile;
            set
            {
                pointerFile = value;

                OnPropertyChanged(nameof(PointerFile));
                OnPropertyChanged(nameof(ItemState));
            }
        }
        private PointerFile pointerFile;

        public BinaryFile BinaryFile
        {
            get => binaryFile;
            set
            {
                binaryFile = value;

                OnPropertyChanged(nameof(BinaryFile));
                OnPropertyChanged(nameof(ItemState));
            }
        }
        private BinaryFile binaryFile;

        public PointerFileEntryAriusEntry PointerFileEntry
        {
            get => pointerFileEntry;
            set
            {
                pointerFileEntry = value;

                OnPropertyChanged(nameof(PointerFileEntry));
                OnPropertyChanged(nameof(ItemState));
            }
        }
        private PointerFileEntryAriusEntry pointerFileEntry;

        public object Manifest => "";


        public string Size
        {
            get
            {
                if (BinaryFile is not null)
                    return BinaryFile.Length.GetBytesReadable(LongExtensions.Size.KB);
                else if (PointerFileEntry is not null)
                    return "TODO";
                else if (PointerFile is not null)
                    return "Unknown";
                else
                    throw new NotImplementedException();
            }
        }

        public string ItemState
        {
            get
            {
                var itemState = new StringBuilder();

                itemState.Append(BinaryFile is not null ? 'Y' : 'N');
                itemState.Append(PointerFile is not null ? 'Y' : 'N');
                itemState.Append(PointerFileEntry is not null ? 'Y' : 'N');
                itemState.Append(Manifest is not null ? 'A' : throw new NotImplementedException());

                return itemState.ToString();
            }
        }
    }

    internal class ItemStateToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return null;

            var bitmap = (Bitmap)Resources.ResourceManager.GetObject(value as string);
            var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            var image = new BitmapImage();
            image.BeginInit();
            ms.Seek(0, SeekOrigin.Begin);
            image.StreamSource = ms;
            image.EndInit();
            return image as ImageSource;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
