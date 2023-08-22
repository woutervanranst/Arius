using Arius.Core.Facade;
using Arius.UI.Properties;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Arius.UI;

/// <summary>
/// Interaction logic for RepositoryChooserWindow.xaml
/// </summary>
public partial class RepositoryChooserWindow : Window
{
    public RepositoryChooserWindow()
    {
        InitializeComponent();
    }
}

public class ChooseRepositoryViewModel : ObservableObject
{
    private readonly Facade               facade;
    private readonly Debouncer            debouncer = new();


    public ChooseRepositoryViewModel(Facade facade)
    {
        this.facade = facade;

        LoadState();

        //LoadContainersCommand       = new RelayCommand(async () => await LoadContainersAsync(), CanLoadContainers);
        SelectLocalDirectoryCommand = new RelayCommand(SelectLocalDirectory);
        OpenRepositoryCommand       = new RelayCommand(OpenRepository, CanOpenRepository);
    }
    

    public string LocalDirectory
    {
        get => localDirectory;
        set => SetProperty(ref localDirectory, value);
    }
    private string localDirectory;


    public ICommand SelectLocalDirectoryCommand { get; }
    private void SelectLocalDirectory()
    {
        using (var dialog = new FolderBrowserDialog())
        {
            dialog.SelectedPath = LocalDirectory;

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                LocalDirectory = dialog.SelectedPath;
            }
        }
    }


    public string AccountName
    {
        get => accountName;
        set
        {
            if (SetProperty(ref accountName, value))
            {
                debouncer.Debounce(async () =>
                {
                    if (CanLoadStorageAccountFacade()) await LoadStorageAccountFacadeAsync();
                });
            }
        }
    }
    private string accountName;


    public string AccountKey
    {
        get => accountKey;
        set
        {
            if (SetProperty(ref accountKey, value))
            {
                debouncer.Debounce(async () =>
                {
                    if (CanLoadStorageAccountFacade()) await LoadStorageAccountFacadeAsync();
                });
            }
        }
    }
    private string accountKey;

    
    public bool StorageAccountError
    {
        get => accountError;
        set => SetProperty(ref accountError, value);
    }
    private bool accountError;


    private bool CanLoadStorageAccountFacade()
    {
        return !string.IsNullOrWhiteSpace(AccountName) && !string.IsNullOrWhiteSpace(AccountKey);
    }
    private async Task LoadStorageAccountFacadeAsync()
    {
        try
        {
            LoadingRemote = true;

            var storageAccountFacade = facade.ForStorageAccount(AccountName, AccountKey);
            ContainerNames = new ObservableCollection<string>(await storageAccountFacade.GetContainerNamesAsync(0).ToListAsync());

            if (ContainerNames.Contains(Settings.Default.SelectedContainerName))
                SelectedContainerName = Settings.Default.SelectedContainerName;
            else if (ContainerNames.Count > 0)
                SelectedContainerName = ContainerNames[0];

            StorageAccountError  = false;
            StorageAccountFacade = storageAccountFacade;
        }
        catch (Exception e)
        {
            StorageAccountError         = true;
            StorageAccountFacade = default;
        }
        finally
        {
            LoadingRemote = false;
        }
    }
    private StorageAccountFacade? StorageAccountFacade { get; set; } = default;
    

    public ObservableCollection<string> ContainerNames
    {
        get => containerNames;
        set => SetProperty(ref containerNames, value);
    }
    private ObservableCollection<string> containerNames;


    public string SelectedContainerName
    {
        get => selectedContainerName;
        set
        {
            if (SetProperty(ref selectedContainerName, value))
            {
                debouncer.Debounce(async () =>
                {
                    if (CanLoadRepositoryFacade()) await LoadRepositoryFacadeAsync();
                });
            }
        }
    }
    private string selectedContainerName;


    public string Passphrase
    {
        get => passphrase;
        set
        {
            if (SetProperty(ref passphrase, value))
            {
                debouncer.Debounce(async () =>
                {
                    if (CanLoadRepositoryFacade()) await LoadRepositoryFacadeAsync();
                });
            }
        }
    }
    private string passphrase;


    public bool RepositoryError
    {
        get => repositoryError;
        set => SetProperty(ref repositoryError, value);
    }
    private bool repositoryError;
    

    private bool CanLoadRepositoryFacade()
    {
        return StorageAccountFacade is not null && !string.IsNullOrWhiteSpace(SelectedContainerName) && !string.IsNullOrWhiteSpace(Passphrase);
    }


    private async Task LoadRepositoryFacadeAsync()
    {
        try
        {
            LoadingRemote = true;

            RepositoryFacade = await StorageAccountFacade!.ForRepositoryAsync(SelectedContainerName, Passphrase);

            RepositoryError = false;
        }
        catch (Exception e)
        {
            RepositoryError  = true;
            RepositoryFacade = default;
        }
        finally
        {
            LoadingRemote = false;
        }
    }
    private RepositoryFacade? RepositoryFacade { get; set; }


    public bool LoadingRemote
    {
        get => loadingRemote;
        set => SetProperty(ref loadingRemote, value);
    }

    private bool loadingRemote;


    public ICommand OpenRepositoryCommand { get; }
    private bool CanOpenRepository()
    {
        if (!Directory.Exists(LocalDirectory))
            return false;

        if (StorageAccountError)
            return false;

        if (string.IsNullOrEmpty(SelectedContainerName))
            return false;

        return true;
    }

    private void OpenRepository()
    {
        SaveState();
    }






    private void LoadState()
    {
        AccountName    = Settings.Default.AccountName;
        AccountKey     = Settings.Default.AccountKey;
        LocalDirectory = Settings.Default.LocalDirectory;
    }

    private void SaveState()
    {
        Settings.Default.AccountName           = AccountName;
        Settings.Default.AccountKey            = AccountKey;
        Settings.Default.LocalDirectory        = LocalDirectory;
        Settings.Default.SelectedContainerName = SelectedContainerName;
        Settings.Default.Save();
    }
}

public class RepositoryChosenMessage
{
    public RepositoryChosenMessage(RepositoryFacade repository)
    {
        ChosenRepository = repository;
    }

    public RepositoryFacade ChosenRepository { get; }
}