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
        CurrentRepository = recentRepositoryManager.GetMostRecent();
        if (CurrentRepository != null)
        {
            WindowName = $"Arius Explorer - {CurrentRepository.LocalDirectory.Name}";
            ArchiveStatistics = $"Repository: {CurrentRepository.LocalDirectoryPath}";

            // TODO Load
        }
        else
        {
            // Initialize with sample data for development
            RootNode = [
                new TreeNodeViewModel("Sample Repository")
                {
                    Folders = [
                        new TreeNodeViewModel("Documents"),
                        new TreeNodeViewModel("Images"),
                        new TreeNodeViewModel("Videos")
                    ]
                }
            ];

            SelectedFolder = new FolderViewModel();
            SelectedItemsText = "No items selected";
            ArchiveStatistics = "Repository not loaded";
        }
    }


    [ObservableProperty]
    private string windowName = "Arius Explorer";

    [RelayCommand]
    private void ViewLoaded()
    {
        // TODO: Initialize view when loaded
        WindowName = "Arius Explorer - Repository Browser";
    }

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string archiveStatistics = "";

    [ObservableProperty]
    private ObservableCollection<RepositoryOptions> recentRepositories = [];

    [ObservableProperty]
    private RepositoryOptions? currentRepository;

    // MENUS

    [RelayCommand] // File > Open...
    private void ChooseRepository()
    {
        var viewModel = dialogService.ShowDialog<ChooseRepository.Window, ChooseRepository.ChooseRepositoryViewModel>(vm =>
        {
            vm.Repository = CurrentRepository;
        });

        // Process the returned repository selection
        if (viewModel.Repository != null)
        {
            CurrentRepository = viewModel.Repository;
            OpenRecentRepository(viewModel.Repository);
        }
    }


    [RelayCommand] // File > Recent > [list]
    private void OpenRecentRepository(RepositoryOptions repository)
    {
        CurrentRepository = repository;
        
        // Use the new service to update recent repositories
        recentRepositoryManager.TouchOrAdd(repository);

        WindowName = $"Arius Explorer - {repository.LocalDirectory.Name}";
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