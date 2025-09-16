using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Arius.Core.Features.Queries.ContainerNames;
using Mediator;
using Unit = System.Reactive.Unit;

namespace Arius.Explorer.ChooseRepository;

public partial class WindowViewModel : ObservableObject, IDisposable
{
    private readonly IMediator     mediator;
    private readonly Subject<Unit> credentialsChangedSubject = new();
    private readonly IDisposable   debounceSubscription;

    [ObservableProperty]
    private string windowName = "Choose Repository";
    
    [ObservableProperty]
    private bool isLoading;
    
    [ObservableProperty]
    private string localDirectoryPath = "";
    
    
    [ObservableProperty]
    private string containerName = "";
    
    [ObservableProperty]
    private ObservableCollection<string> containerNames = [];
    
    [ObservableProperty]
    private string passphrase = "";
    
    [ObservableProperty]
    private bool storageAccountError;

    public WindowViewModel(IMediator mediator)
    {
        this.mediator = mediator;
        // Initialize with sample data for development
        LocalDirectoryPath = @"C:\SampleRepository";
        AccountName = "samplestorageaccount";
        ContainerNames = [
            "container1",
            "container2",
            "backups",
            "archives"
        ];
        ContainerName = "container1";

        // Set up debouncing for Storage Account credential changes
        debounceSubscription = credentialsChangedSubject
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Where(_ => !string.IsNullOrWhiteSpace(AccountName) && !string.IsNullOrWhiteSpace(AccountKey))
            .Select(_ => Observable.FromAsync(LoadContainersAsync))
            .Switch() // cancels previous LoadContainersAsync if new values arrive
            .ObserveOn(SynchronizationContext.Current!) // marshal back to UI thread
            .Subscribe();
    }

    // -- ACCOUNT NAME & ACCOUNT KEY

    [ObservableProperty]
    private string accountName = "";

    [ObservableProperty]
    private string accountKey = "";

    partial void OnAccountNameChanged(string value) => credentialsChangedSubject.OnNext(Unit.Default);

    partial void OnAccountKeyChanged(string value) => credentialsChangedSubject.OnNext(Unit.Default);

    private async Task LoadContainersAsync()
    {
        try
        {
            IsLoading = true;

            var query = new ContainerNamesQuery()
            {
                AccountName = AccountName,
                AccountKey  = AccountKey
            };

            var r = await mediator.CreateStream(query).ToListAsync();





            //var storageAccountFacade = facade.ForStorageAccount(AccountName, AccountKey);
            //ContainerNames = new ObservableCollection<string>(await storageAccountFacade.GetContainerNamesAsync(0).ToListAsync());

            if (ContainerName is null && ContainerNames.Count > 0)
                ContainerName = ContainerNames[0];

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

    // -- OTHER STUFF

    [RelayCommand]
    private void SelectLocalDirectory()
    {
        // TODO: Implement folder browser dialog
        LocalDirectoryPath = @"C:\Users\Sample\Documents\MyRepository";
    }

    [RelayCommand]
    private void OpenRepository()
    {
        // TODO: Validate inputs and open repository
        // For now just close the dialog
        IsLoading = true;
        // Simulate some work
        IsLoading = false;
    }

    // -- DISPOSE

    public void Dispose()
    {
        debounceSubscription?.Dispose();
        credentialsChangedSubject?.Dispose();
    }
}