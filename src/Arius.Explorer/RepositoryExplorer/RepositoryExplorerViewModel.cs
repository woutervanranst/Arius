using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace Arius.Explorer.RepositoryExplorer;

public partial class RepositoryExplorerViewModel : ObservableObject
{
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
    private ObservableCollection<string> recentRepositories = [];

    public RepositoryExplorerViewModel()
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
        
        RecentRepositories = [
            "C:\\Archives\\Project1",
            "C:\\Archives\\Project2",
            "D:\\Backup\\Documents"
        ];
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
        if (App.ServiceProvider == null) return;
        
        var chooseRepositoryWindow = App.ServiceProvider.GetRequiredService<ChooseRepository.Window>();
        chooseRepositoryWindow.ShowDialog();
    }

    [RelayCommand]
    private void OpenRecentRepository(string repositoryPath)
    {
        // TODO: Implement opening recent repository
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
}