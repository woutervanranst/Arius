using Arius.Explorer.Models;
using Arius.Explorer.Settings;
using Arius.Explorer.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Arius.Explorer.RepositoryExplorer;

public partial class RepositoryExplorerViewModel : ObservableObject
{
    private readonly IApplicationSettings settings;
    private readonly IDialogService dialogService;

    // -- INITIALIZATION & GENERAL WINDOW

    public RepositoryExplorerViewModel(IApplicationSettings settings, IDialogService dialogService)
    {
        this.settings = settings;
        this.dialogService = dialogService;

        // Load recent repositories from settings
        LoadRecentRepositories();

        // Check for last opened repository and auto-open if exists
        CurrentRepository = settings.LastOpenedRepository;
        if (CurrentRepository != null)
        {
            WindowName = $"Arius Explorer - {CurrentRepository.LocalDirectory.Name}";
            ArchiveStatistics = $"Repository: {CurrentRepository.LocalDirectoryPath}";
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

    private void LoadRecentRepositories()
    {
        var recent = settings.RecentRepositories;
        RecentRepositories = new ObservableCollection<RepositoryOptions>(recent);
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
        var viewModel = dialogService.ShowDialog<ChooseRepository.Window, ChooseRepository.WindowViewModel>(vm =>
        {
            if (CurrentRepository != null)
            {
                vm.LocalDirectoryPath = CurrentRepository.LocalDirectoryPath;
                vm.AccountName        = CurrentRepository.AccountName;
                vm.AccountKey         = CurrentRepository.AccountKey; // This will decrypt the protected value
                vm.ContainerName      = CurrentRepository.ContainerName;
                vm.Passphrase         = CurrentRepository.Passphrase; // This will decrypt the protected value
            }
        });

        // TODO: Process the returned viewModel if needed
    }


    [RelayCommand] // File > Recent > [list]
    private void OpenRecentRepository(RepositoryOptions repository)
    {
        CurrentRepository = repository;
        settings.SetLastOpenedRepository(repository);

        // Update the last opened time
        repository.LastOpened = DateTime.Now;
        settings.AddRecentRepository(repository);

        WindowName        = $"Arius Explorer - {repository.LocalDirectory.Name}";
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