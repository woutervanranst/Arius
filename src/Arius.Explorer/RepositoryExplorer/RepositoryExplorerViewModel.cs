using Arius.Explorer.Settings;
using Arius.Explorer.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;

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

            RootNode = [];
            SelectedFolder = new FolderViewModel();
            SelectedItemsText = "";
            ArchiveStatistics = "";
        }
    }

    [RelayCommand]
    private void ViewLoaded()
    {
        // TODO: Initialize view when loaded
        if (Repository == null)
            ChooseRepository();
    }

    [ObservableProperty]
    private string windowName = "Arius Explorer";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string archiveStatistics = "";

    // -- REPOSITORY

    [ObservableProperty]
    private RepositoryOptions? repository;

    partial void OnRepositoryChanged(RepositoryOptions? value)
    {
        WindowName = value == null
            ? $"{App.Name} - No Repository"
            : $"{App.Name}: {value}";
    }






    // MENUS

    //      File > Open...

    [RelayCommand] 
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

    //      File > Recent > [list]

    [ObservableProperty]
    private ObservableCollection<RepositoryOptions> recentRepositories = [];

    [RelayCommand] 
    private void OpenRepository(RepositoryOptions repository)
    {
        Repository = repository;

        // Use the new service to update recent repositories
        recentRepositoryManager.TouchOrAdd(repository);

        ArchiveStatistics = $"Repository: {repository.LocalDirectoryPath}";

        // TODO: Actually load the repository data
    }

    //      About

    [RelayCommand]
    private void About()
    {
        // TODO: Show about dialog
        MessageBox.Show("h");
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