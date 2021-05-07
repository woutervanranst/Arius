using Arius.Facade;
using Arius.UI.Extensions;
using Arius.UI.Properties;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Arius.UI.ViewModels
{
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

            Versions = new((await azureRepositoryFacade.GetVersionsAsync()).Select(v => v.ToLocalTime()).Reverse());
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

                ReloadItems().ConfigureAwait(false);
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
                OnPropertyChanged();

                ReloadItems().ConfigureAwait(false);
            }
        }
        private bool includeDeletedItems = false;


        private async Task ReloadItems()
        {
            // Clear TreeView
            var root = GetRoot();
            root.Clear();

            // Load Local
            LoadLocalEntries();

            // Load Remote
            LoadRepositoryEntries();
        }


        // -- REPOSITORY ENTRIES

        private async Task LoadRepositoryEntries()
        {
            try
            {
                if (SelectedVersion == DateTime.MinValue)
                    return;

                LoadingRemote = true;

                var root = GetRoot();

                await foreach (var item in azureRepositoryFacade.GetRemoteEntries(SelectedVersion.ToUniversalTime(), IncludeDeletedItems))
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

                //LoadLocalEntries().ConfigureAwait(false);
                ReloadItems();
            }
        }
        private string localPath;

        private async Task LoadLocalEntries()
        {
            try
            {
                if (string.IsNullOrEmpty(LocalPath))
                    return;

                LoadingLocal = true;

                var root = GetRoot();

                var di = new DirectoryInfo(LocalPath);

                await foreach (var item in facade.GetLocalEntries(di))
                    root.Add(item);

                //OnPropertyChanged(nameof(Folders));
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
            }
            finally
            {
                LoadingLocal = false;
            }
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
}
