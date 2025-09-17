using Arius.Explorer.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Explorer.ChooseRepository;

public partial class WindowViewModel : ObservableObject
{
    private readonly IRepositoryConnectionService repositoryConnectionService;
    private readonly IFolderDialogService folderDialogService;

    [ObservableProperty]
    private string windowName = "Choose Repository";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string localDirectoryPath = string.Empty;

    [ObservableProperty]
    private string accountName = string.Empty;

    [ObservableProperty]
    private string accountKey = string.Empty;

    [ObservableProperty]
    private string containerName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> containerNames = new();

    [ObservableProperty]
    private string passphrase = string.Empty;

    [ObservableProperty]
    private bool storageAccountError;

    [ObservableProperty]
    private string storageAccountErrorMessage = string.Empty;

    public WindowViewModel(IRepositoryConnectionService repositoryConnectionService, IFolderDialogService folderDialogService)
    {
        this.repositoryConnectionService = repositoryConnectionService ?? throw new ArgumentNullException(nameof(repositoryConnectionService));
        this.folderDialogService = folderDialogService ?? throw new ArgumentNullException(nameof(folderDialogService));

        // Provide deterministic data to make the development experience pleasant while keeping the behaviour testable.
        LocalDirectoryPath = @"C:\SampleRepository";
        AccountName = "samplestorageaccount";
        ContainerNames = new ObservableCollection<string>
        {
            "container1",
            "container2",
            "backups",
            "archives"
        };
        ContainerName = ContainerNames.FirstOrDefault() ?? string.Empty;
    }

    partial void OnLocalDirectoryPathChanged(string value) => NotifyOpenRepositoryCanExecuteChanged();
    partial void OnAccountNameChanged(string value) => NotifyOpenRepositoryCanExecuteChanged();
    partial void OnAccountKeyChanged(string value) => NotifyOpenRepositoryCanExecuteChanged();

    private void NotifyOpenRepositoryCanExecuteChanged()
    {
        OpenRepositoryCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectLocalDirectory()
    {
        var selectedPath = folderDialogService.BrowseForFolder(LocalDirectoryPath);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            LocalDirectoryPath = selectedPath;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenRepository))]
    private async Task OpenRepositoryAsync()
    {
        IsLoading = true;

        try
        {
            var request = new RepositoryConnectionRequest(LocalDirectoryPath, AccountName, AccountKey, ContainerName, Passphrase);
            var result = await repositoryConnectionService.TryConnectAsync(request).ConfigureAwait(false);

            StorageAccountError = !result.IsSuccess;
            StorageAccountErrorMessage = result.ErrorMessage ?? string.Empty;

            if (!result.IsSuccess)
            {
                return;
            }

            var containers = result.ContainerNames;

            ContainerNames = new ObservableCollection<string>(containers);

            if (containers.Count == 0)
            {
                ContainerName = string.Empty;
            }
            else if (!containers.Contains(ContainerName, StringComparer.OrdinalIgnoreCase))
            {
                ContainerName = containers[0];
            }
        }
        catch (ArgumentException ex)
        {
            StorageAccountError = true;
            StorageAccountErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanOpenRepository()
    {
        return !string.IsNullOrWhiteSpace(LocalDirectoryPath)
            && !string.IsNullOrWhiteSpace(AccountName)
            && !string.IsNullOrWhiteSpace(AccountKey);
    }
}
