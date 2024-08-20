using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Arius.Core.Domain.Storage;
using Arius.Core.Facade;
using Arius.Core.Queries.ContainerNames;
using Arius.UI.Messages;
using Arius.UI.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace Arius.UI.ViewModels;

internal partial class ChooseRepositoryViewModel : ObservableRecipient, IRepositoryOptionsProvider
{
    private readonly IMediator mediator;

    public ChooseRepositoryViewModel(IMediator mediator)
    {
        this.mediator               = mediator;
        SelectLocalDirectoryCommand = new RelayCommand(SelectLocalDirectory);
        OpenRepositoryCommand       = new AsyncRelayCommand(OpenRepositoryAsync);
    }

    public string WindowName => $"Choose repository";


    [ObservableProperty]
    private DirectoryInfo localDirectory;


    public ICommand SelectLocalDirectoryCommand { get; }
    private void SelectLocalDirectory()
    {
        using var dialog = new FolderBrowserDialog();
        dialog.SelectedPath = LocalDirectory?.FullName ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            LocalDirectory = new(dialog.SelectedPath);
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
                    Application.Current.Dispatcher.InvokeAsync(LoadStorageAccountFacadeAsync, DispatcherPriority.Background);
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
                    Application.Current.Dispatcher.InvokeAsync(LoadStorageAccountFacadeAsync, DispatcherPriority.Background);
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

            var q = new ContainerNamesQuery(AccountName, AccountKey);
            var r = await mediator.Send(q);
            ContainerNames = new ObservableCollection<string>(await r.ToListAsync());

            if (ContainerName is null && ContainerNames.Count > 0)
                ContainerName = ContainerNames[0];

            StorageAccountError  = false;
        }
        catch (Exception e)
        {
            StorageAccountError  = true;
        }
        finally
        {
            IsLoading = false;
        }
    }
    

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
        if (!Directory.Exists(LocalDirectory.FullName))
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