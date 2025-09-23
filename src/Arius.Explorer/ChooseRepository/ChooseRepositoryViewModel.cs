using Arius.Core.Features.Queries.ContainerNames;
using Arius.Explorer.Settings;
using Arius.Explorer.Shared.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mediator;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows;
using Unit = System.Reactive.Unit;

namespace Arius.Explorer.ChooseRepository;

public partial class ChooseRepositoryViewModel : ObservableObject, IDisposable
{
    private readonly IMediator                mediator;
    private readonly Subject<Unit>            credentialsChangedSubject = new();
    private readonly IDisposable              debounceSubscription;

    [ObservableProperty]
    private string windowName = "Choose Repository";


    public ChooseRepositoryViewModel(IMediator mediator, IRecentRepositoryManager recentRepositoryManager)
    {
        this.mediator = mediator;

        // Set up debouncing for Storage Account credential changes
        debounceSubscription = credentialsChangedSubject
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Where(_ => !string.IsNullOrWhiteSpace(AccountName) && !string.IsNullOrWhiteSpace(AccountKey))
            .Select(_ => Observable.FromAsync(OnStorageAccountCredentialsChanged))
            .Switch() // cancels previous OnStorageAccountCredentialsChanged if new values arrive
            .ObserveOn(SynchronizationContext.Current!) // marshal back to UI thread
            .Subscribe();
    }

    // -- REPOSITORY

    [ObservableProperty]
    private RepositoryOptions? repository;

    partial void OnRepositoryChanged(RepositoryOptions? value)
    {
        if (value != null)
        {
            LocalDirectoryPath = value.LocalDirectoryPath;
            AccountName        = value.AccountName;
            AccountKey         = value.AccountKey;
            ContainerName      = value.ContainerName;
            Passphrase         = value.Passphrase;
        }
    }


    // -- LOCAL PATH

    [ObservableProperty]
    private string localDirectoryPath = "";

    [RelayCommand]
    private void SelectLocalDirectory()
    {
        var folderDialog = new OpenFolderDialog
        {
            Title            = "Select Folder",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (folderDialog.ShowDialog() == true)
        {
            LocalDirectoryPath = folderDialog.FolderName;
        }
    }


    // -- ACCOUNT NAME & ACCOUNT KEY

    [ObservableProperty]
    private string accountName = "";

    [ObservableProperty]
    private string accountKey = "";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool storageAccountError;

    partial void OnAccountNameChanged(string value) => credentialsChangedSubject.OnNext(Unit.Default);

    partial void OnAccountKeyChanged(string value) => credentialsChangedSubject.OnNext(Unit.Default);

    private async Task OnStorageAccountCredentialsChanged(CancellationToken cancellationToken)
    {
        // The AccountName / AccountKey has changed - load the containers

        try
        {
            IsLoading = true;

            var query = new ContainerNamesQuery()
            {
                AccountName = AccountName,
                AccountKey  = AccountKey
            };

            //var r = await mediator.CreateStream(query).ToListAsync();


            //var storageAccountFacade = facade.ForStorageAccount(AccountName, AccountKey);
            //ContainerNames = new ObservableCollection<string>(await storageAccountFacade.GetContainerNamesAsync(0).ToListAsync());

            //if (ContainerName is null && ContainerNames.Count > 0)
            //    ContainerName = ContainerNames[0];

            StorageAccountError = false;
        }
        catch (Exception e)
        {
            StorageAccountError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    // -- CONTAINERNAME

    [ObservableProperty]
    private string containerName = "";

    [ObservableProperty]
    private ObservableCollection<string> containerNames = [];

    // -- PASSPHRASE

    [ObservableProperty]
    private string passphrase = "";
    

    [RelayCommand]
    private void OpenRepository()
    {
        try
        {
            IsLoading = true;

            // Create repository options from current form data
            var repositoryOptions = new RepositoryOptions
            {
                LocalDirectoryPath  = LocalDirectoryPath,
                AccountName         = AccountName ?? "",
                AccountKeyProtected = string.IsNullOrEmpty(AccountKey) ? "" : AccountKey.Protect(),
                ContainerName       = ContainerName ?? "",
                PassphraseProtected = string.IsNullOrEmpty(Passphrase) ? "" : Passphrase.Protect(),
            };

            // Set the repository for return to parent ViewModel
            Repository = repositoryOptions;

            // Close the dialog with OK result
            var window = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.DataContext == this);
            if (window != null)
            {
                window.DialogResult = true;
                window.Close();
            }
        }
        catch (Exception)
        {
            // TODO: Handle error - show message to user
        }
        finally
        {
            IsLoading = false;
        }
    }



    // -- DISPOSE

    public void Dispose()
    {
        debounceSubscription?.Dispose();
        credentialsChangedSubject?.Dispose();
    }
}