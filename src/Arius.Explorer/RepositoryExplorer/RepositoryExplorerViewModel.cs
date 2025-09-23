using Arius.Explorer.Settings;
using Arius.Explorer.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Arius.Explorer.RepositoryExplorer;

public partial class RepositoryExplorerViewModel : ObservableObject
{
    private readonly IApplicationSettings settings;
    private readonly IRecentRepositoryManager recentRepositoryManager;
    private readonly IDialogService dialogService;

    // -- INITIALIZATION & GENERAL WINDOW

    public RepositoryExplorerViewModel(IApplicationSettings settings, IRecentRepositoryManager recentRepositoryManager, IDialogService dialogService)
    {
        this.settings                = settings;
        this.recentRepositoryManager = recentRepositoryManager;
        this.dialogService           = dialogService;

        // Load recent repositories from settings
        RecentRepositories = settings.RecentRepositories;

        // Check for most recent repository and auto-open if exists
        Repository = recentRepositoryManager.GetMostRecent();
        if (Repository != null)
        {
            ArchiveStatistics = $"Repository: {Repository.LocalDirectoryPath}";

            // TODO Load
        }
        else
        {
            ArchiveStatistics = "";

            RootNode          = [];
            SelectedFolder    = new FolderViewModel();
            SelectedItemsText = "";
            ArchiveStatistics = "";
        }
    }


    [ObservableProperty]
    private string windowName = "Arius Explorer";

    [RelayCommand]
    private void ViewLoaded()
    {
        // TODO: Initialize view when loaded
    }

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string archiveStatistics = "";

    [ObservableProperty]
    private ObservableCollection<RepositoryOptions> recentRepositories = [];

    [ObservableProperty]
    private RepositoryOptions? repository;

    partial void OnRepositoryChanged(RepositoryOptions? value)
    {
        WindowName = value == null
            ? $"{App.Name} - No Repository"
            : $"{App.Name}: {value}";
    }

    // MENUS

    [RelayCommand] // File > Open...
    private void ChooseRepository()
    {
        // Show dialog and handle result
        var openedRepository = dialogService.ShowChooseRepositoryDialog(Repository);
        if (openedRepository != null)
        {
            Repository = openedRepository;
            OpenRepository(openedRepository);
        }
    }


    [RelayCommand] // File > Recent > [list]
    private void OpenRepository(RepositoryOptions repository)
    {
        Repository = repository;

        // Use the new service to update recent repositories
        recentRepositoryManager.TouchOrAdd(repository);

        ArchiveStatistics = $"Repository: {repository.LocalDirectoryPath}";

        // TODO: Actually load the repository data
    }

    [RelayCommand]
    private void About()
    {
        // TODO: Show about dialog
    }



    // TREEVIEW

    [ObservableProperty]
    private ObservableCollection<TreeNodeViewModel> rootNode = [];

    [ObservableProperty]
    private FolderViewModel? selectedFolder;

    
    // LISTVIEW

    [ObservableProperty]
    private string selectedItemsText = "";

    [RelayCommand]
    private void Restore()
    {
        // TODO: Implement restore functionality
    }
}