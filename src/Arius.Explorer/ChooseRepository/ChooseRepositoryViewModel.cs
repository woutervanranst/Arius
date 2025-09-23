using Arius.Core.Features.Queries.ContainerNames;
using Arius.Explorer.Settings;
using Arius.Explorer.Shared.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mediator;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Unit = System.Reactive.Unit;

namespace Arius.Explorer.ChooseRepository;

public partial class ChooseRepositoryViewModel : ObservableObject, IDisposable
{
    private readonly IMediator              mediator;
    private readonly Subject<Unit>          credentialsChangedSubject = new();
    private readonly IDisposable            debounceSubscription;
    private readonly TimeSpan               credentialsDebounce;
    private readonly SynchronizationContext synchronizationContext;

    [ObservableProperty]
    private string windowName = "Choose Repository";
    public ChooseRepositoryViewModel(
        IMediator              mediator,
        TimeSpan?              credentialsDebounce = null)
    {
        this.mediator            = mediator;
        this.credentialsDebounce = credentialsDebounce ?? TimeSpan.FromMilliseconds(500);
        synchronizationContext   = SynchronizationContext.Current ?? new SynchronizationContext();

        // Set up debouncing for Storage Account credential changes
        debounceSubscription = credentialsChangedSubject
            .Throttle(this.credentialsDebounce)
            .Where(_ => !string.IsNullOrWhiteSpace(AccountName) && !string.IsNullOrWhiteSpace(AccountKey))
            .Select(_ => Observable.FromAsync(OnStorageAccountCredentialsChanged))
            .Switch() // cancels previous OnStorageAccountCredentialsChanged if new values arrive
            .ObserveOn(synchronizationContext) // marshal back to UI thread when available
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
            StorageAccountError = false;

            var query = new ContainerNamesQuery
            {
                AccountName = AccountName,
                AccountKey  = AccountKey
            };

            var containers = new List<string>();

            await foreach (var container in mediator.CreateStream(query, cancellationToken))
            {
                containers.Add(container);
            }

            var updated = new ObservableCollection<string>(containers);
            ContainerNames = updated;

            if (updated.Count == 0)
            {
                ContainerName = string.Empty;
            }
            else if (!updated.Contains(ContainerName))
            {
                ContainerName = updated.First();
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected when new credentials are entered; do not flag as error
        }
        catch (Exception)
        {
            StorageAccountError = true;
            ContainerNames      = [];
            ContainerName       = string.Empty;
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

            var window = Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(w => Equals(w.DataContext, this));

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