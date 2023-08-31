using Arius.Core.Facade;
using Arius.UI.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;

namespace Arius.UI.ViewModels;

public partial class RepositoryChooserViewModel : ObservableRecipient
{
    private readonly Facade     facade;
    private readonly Debouncer  debouncer = new();


    public RepositoryChooserViewModel(Facade facade)
    {
        this.facade    = facade;

        SelectLocalDirectoryCommand = new RelayCommand(SelectLocalDirectory);
        OpenRepositoryCommand       = new AsyncRelayCommand(OpenRepositoryAsync);
        Messenger.Register<ChooseRepositoryMessage>(this, OnChooseRepository);

    private void OnChooseRepository(object recipient, ChooseRepositoryMessage message)
    {
        if (message is not null)
        {
            AccountName    = message.AccountName;
            AccountKey     = message.AccountKey;
            LocalDirectory = message.LocalDirectory.FullName;
            Passphrase     = message.Passphrase;
        }
    }

    public string WindowName => $"Choose repository";


    [ObservableProperty]
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

    
    [ObservableProperty]
    private bool storageAccountError;


    private bool CanLoadStorageAccountFacade()
    {
        return !string.IsNullOrWhiteSpace(AccountName) && !string.IsNullOrWhiteSpace(AccountKey);
    }
    private async Task LoadStorageAccountFacadeAsync()
    {
        try
        {
            IsLoading = true;

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
            StorageAccountError  = true;
            StorageAccountFacade = default;
        }
        finally
        {
            IsLoading = false;
        }
    }
    private StorageAccountFacade? StorageAccountFacade { get; set; } = default;
    

    [ObservableProperty]
    private ObservableCollection<string> containerNames;


    [ObservableProperty]
    private string selectedContainerName;


    [ObservableProperty]
    private string passphrase;


    [ObservableProperty]
    private bool isLoading;


    public ICommand OpenRepositoryCommand { get; }
    private async Task OpenRepositoryAsync()
    {
        if (!Directory.Exists(LocalDirectory))
        {
            MessageBox.Show("The local directory does not exist. Please select a valid directory.", App.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (StorageAccountError)
        {
            MessageBox.Show("There was an error with the storage account. Please ensure your account details are correct.", App.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(SelectedContainerName))
        {
            MessageBox.Show("No container is selected. Please select a container before proceeding.", App.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(Passphrase))
        {
            MessageBox.Show("Passphrase cannot be empty. Please enter a valid passphrase.", App.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsLoading = true;

            var repositoryFacade = await StorageAccountFacade!.ForRepositoryAsync(SelectedContainerName, Passphrase);

            SaveState();

            messenger.Send(new RepositoryChosenMessage(new DirectoryInfo(LocalDirectory), repositoryFacade));
        }
        catch (ArgumentException e)
        {
            IsLoading = false;
            MessageBox.Show("Invalid password.", App.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception e)
        {
            IsLoading = false;
            MessageBox.Show(e.Message, App.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
        finally
        {
            IsLoading = false;
        }
    }



    }
}