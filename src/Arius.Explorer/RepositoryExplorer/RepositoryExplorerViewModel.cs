using Arius.Explorer.Models;
using Arius.Explorer.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace Arius.Explorer.RepositoryExplorer;

public partial class RepositoryExplorerViewModel : ObservableObject
{
    private readonly IApplicationSettings settings;

    [ObservableProperty]
    private string windowName = "Arius Explorer";
    
    [ObservableProperty]
    private bool isLoading;
    
    [ObservableProperty]
    private string selectedItemsText = "";
    
    [ObservableProperty]
    private string archiveStatistics = "";
    
    [ObservableProperty]
    private ObservableCollection<TreeNodeViewModel> rootNode = [];
    
    [ObservableProperty]
    private FolderViewModel? selectedFolder;
    
    [ObservableProperty]
    private ObservableCollection<RepositoryOptions> recentRepositories = [];

    [ObservableProperty]
    private RepositoryOptions? currentRepository;

    public RepositoryExplorerViewModel(IApplicationSettings settings)
    {
        this.settings = settings;

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

    [RelayCommand]
    private void ViewLoaded()
    {
        // TODO: Initialize view when loaded
        WindowName = "Arius Explorer - Repository Browser";
    }

    [RelayCommand]
    private void ChooseRepository()
    {
        OpenChooseRepositoryWindow(CurrentRepository ?? settings.LastOpenedRepository);
    }

    [RelayCommand]
    private void ChooseNewRepository()
    {
        OpenChooseRepositoryWindow(null);
    }

    private void OpenChooseRepositoryWindow(RepositoryOptions? repositoryToLoad)
    {
        if (App.ServiceProvider == null) return;

        var chooseRepositoryWindow = App.ServiceProvider.GetRequiredService<ChooseRepository.Window>();
        var chooseRepositoryViewModel = App.ServiceProvider.GetRequiredService<ChooseRepository.WindowViewModel>();

        // Load the specified repository or leave empty for new repository
        chooseRepositoryViewModel.LoadRepository(repositoryToLoad);

        chooseRepositoryWindow.ShowDialog();
    }

    [RelayCommand]
    private void OpenRecentRepository(RepositoryOptions repository)
    {
        CurrentRepository = repository;
        settings.SetLastOpenedRepository(repository);

        // Update the last opened time
        repository.LastOpened = DateTime.Now;
        settings.AddRecentRepository(repository);

        WindowName = $"Arius Explorer - {repository.LocalDirectory.Name}";
        ArchiveStatistics = $"Repository: {repository.LocalDirectoryPath}";

        // TODO: Actually load the repository data
    }

    [RelayCommand]
    private void Restore()
    {
        // TODO: Implement restore functionality
    }

    [RelayCommand]
    private void About()
    {
        // TODO: Show about dialog
    }

    private void LoadRecentRepositories()
    {
        var recent = settings.RecentRepositories;
        RecentRepositories = new ObservableCollection<RepositoryOptions>(recent);
    }
}