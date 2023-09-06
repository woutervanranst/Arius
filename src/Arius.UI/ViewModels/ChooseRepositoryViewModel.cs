using Arius.Core.Facade;
using Arius.UI.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Arius.UI.Messages;
using MessageBox = System.Windows.MessageBox;

namespace Arius.UI.ViewModels;

internal partial class ChooseRepositoryViewModel : ObservableRecipient, IRepositoryOptionsProvider
{
    private readonly Facade                 facade;

    public ChooseRepositoryViewModel(Facade facade)
    {
        this.facade = facade;

        SelectLocalDirectoryCommand = new RelayCommand(SelectLocalDirectory);
        OpenRepositoryCommand       = new AsyncRelayCommand(OpenRepositoryAsync);
    }

    public string WindowName => $"Choose repository";


    [ObservableProperty]
    private string localDirectoryFullName;

    public DirectoryInfo LocalDirectory => new(localDirectoryFullName);


    public ICommand SelectLocalDirectoryCommand { get; }
    private void SelectLocalDirectory()
    {
        using var dialog = new FolderBrowserDialog();
        dialog.SelectedPath = LocalDirectory.FullName;

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            LocalDirectoryFullName = dialog.SelectedPath;
        }
    }


    public string AccountName
    {
        get => accountName;
        set
        {
            if (SetProperty(ref accountName, value))
            {
                if (CanLoadStorageAccountFacade())
                {
                    System.Windows.Application.Current.Dispatcher.InvokeAsync(LoadStorageAccountFacadeAsync, DispatcherPriority.Background);
                }
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
                if (CanLoadStorageAccountFacade())
                {
                    System.Windows.Application.Current.Dispatcher.InvokeAsync(LoadStorageAccountFacadeAsync, DispatcherPriority.Background);
                }
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
    private string containerName;


    [ObservableProperty]
    private string passphrase;


    [ObservableProperty]
    private bool isLoading;


    public ICommand OpenRepositoryCommand { get; }
    private async Task OpenRepositoryAsync()
    {
        if (!Directory.Exists(LocalDirectoryFullName))
        {
            MessageBox.Show("The local directory does not exist. Please select a valid directory.", App.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (StorageAccountError)
        {
            MessageBox.Show("There was an error with the storage account. Please ensure your account details are correct.", App.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(ContainerName))
        {
            MessageBox.Show("No container is selected. Please select a container before proceeding.", App.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(Passphrase))
        {
            MessageBox.Show("Passphrase cannot be empty. Please enter a valid passphrase.", App.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        WeakReferenceMessenger.Default.Send(new CloseChooseRepositoryWindowMessage());
    }
}

