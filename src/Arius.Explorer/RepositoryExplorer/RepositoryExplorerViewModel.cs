using Arius.Core.Features.Queries.PointerFileEntries;
using Arius.Explorer.Settings;
using Arius.Explorer.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mediator;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;

namespace Arius.Explorer.RepositoryExplorer;

public partial class RepositoryExplorerViewModel : ObservableObject
{
    private readonly IApplicationSettings settings;
    private readonly IRecentRepositoryManager recentRepositoryManager;
    private readonly IDialogService dialogService;
    private readonly IMediator mediator;

    // -- INITIALIZATION & GENERAL WINDOW

    public RepositoryExplorerViewModel(IApplicationSettings settings, IRecentRepositoryManager recentRepositoryManager, IDialogService dialogService, IMediator mediator)
    {
        this.settings                = settings;
        this.recentRepositoryManager = recentRepositoryManager;
        this.dialogService           = dialogService;
        this.mediator                = mediator;

        // Load recent repositories from settings
        RecentRepositories = settings.RecentRepositories;

        // Check for most recent repository and auto-open if exists
        var mostRecent = recentRepositoryManager.GetMostRecent();
        if (mostRecent != null)
        {
            Repository = mostRecent; // this will trigger OnRepositoryChanged for UI updates
            _ = LoadRepositoryAsync(); // fire-and-forget for initial load
        }
    }

    [RelayCommand] // triggered by View's Loaded event
    private async Task ViewLoaded()
    {
        // If the Explorer window is shown but no Repository is selected, open the ChooseRepository window
        if (Repository == null)
            await OpenChooseRepositoryDialogAsync();
    }

    [ObservableProperty]
    private string windowName = "Arius Explorer";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string archiveStatistics = "";
    

    // MENUS

    //      File > Open...
    [RelayCommand] 
    private async Task OpenChooseRepositoryDialogAsync()
    {
        // Show dialog and handle result
        var openedRepository = dialogService.ShowChooseRepositoryDialog(Repository);
        if (openedRepository != null)
        {
            await OpenRepositoryAsync(openedRepository);
        }
    }

    //      File > Recent > [list]
    [ObservableProperty]
    private ObservableCollection<RepositoryOptions> recentRepositories = [];

    [RelayCommand]
    private async Task OpenRepositoryAsync(RepositoryOptions repository)
    {
        // Use the new service to update recent repositories
        recentRepositoryManager.TouchOrAdd(repository);

        Repository = repository;

        // Load repository data asynchronously
        if (repository != null)
        {
            await LoadRepositoryAsync();
        }
    }


    // -- REPOSITORY

    [ObservableProperty]
    private RepositoryOptions? repository;

    private async Task LoadRepositoryAsync()
    {
        if (Repository == null)
        {
            WindowName        = $"{App.Name} - No Repository";
            RootNode          = [];
            SelectedTreeNode  = null;
            ArchiveStatistics = "";
            SelectedItemsText = "";
        }
        else
        {
            WindowName        = $"{App.Name}: {Repository}";
            ArchiveStatistics = "Statistics TODO";

            IsLoading = true;
            try
            {
                // Create root node
                var rootNode = new TreeNodeViewModel("/", OnNodeSelected)
                {
                    Name = "Root",
                    IsSelected = true,
                    IsExpanded = true
                };

                RootNode         = [rootNode];
                SelectedTreeNode = rootNode;

                ArchiveStatistics = "Statistics TODO";
                SelectedItemsText = "";
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    private async void OnNodeSelected(TreeNodeViewModel selectedNode)
    {
        // Load the content for the selected node
        await LoadNodeContentAsync(selectedNode);
    }

    private async Task LoadNodeContentAsync(TreeNodeViewModel node)
    {
        try
        {
            if (Repository == null)
                return;

            var query = new PointerFileEntriesQuery
            {
                AccountName = Repository.AccountName,
                AccountKey = Repository.AccountKey,
                ContainerName = Repository.ContainerName,
                Passphrase = Repository.Passphrase,
                LocalPath = new DirectoryInfo(Repository.LocalDirectoryPath),
                Prefix = node.Prefix
            };

            // Initialize collections for streaming updates
            node.Folders = [];
            node.Items = [];

            // Update the selected tree node reference for ListView binding immediately
            SelectedTreeNode = node;
            SelectedItemsText = "Loading...";

            var results = mediator.CreateStream(query);

            await foreach (var result in results)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    switch (result)
                    {
                        case PointerFileEntriesQueryDirectoryResult directory:
                            var dirName = ExtractDirectoryName(directory.RelativeName);
                            var childNode = new TreeNodeViewModel(directory.RelativeName, OnNodeSelected)
                            {
                                Name = dirName
                            };

                            node.Folders.Add(childNode);

                            break;

                        case PointerFileEntriesQueryFileResult file:
                            var fileItem = new FileItemViewModel(file);

                            node.Items.Add(fileItem);
                            SelectedItemsText = $"{node.Items.Count} items";

                            break;
                    }
                });
            }

            // Final count update (in case there were only directories)
            //Application.Current.Dispatcher.Invoke(() =>
            //{
            SelectedItemsText = $"{node.Items.Count} items";
            //});
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            //throw;
        }
    }





    //      About

    [RelayCommand]
    private void About()
    {
        var explorerClickOnceVersion = Environment.GetEnvironmentVariable("ClickOnce_CurrentVersion") ?? "unknown"; // https://stackoverflow.com/a/75263211/1582323  //System.Deployment. System.Reflection.Assembly.GetEntryAssembly().GetName().Version; doesnt work
        var explorerVersion          = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "unknown";
        var x                        = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        var coreVersion              = typeof(Arius.Core.Bootstrapper).Assembly.GetName().Version;

        MessageBox.Show($"""
                         Arius Explorer v{explorerVersion}, ClickOnce v{explorerClickOnceVersion}, Assembly v{x}
                         Arius Core v{coreVersion}
                         """, App.Name, MessageBoxButton.OK, MessageBoxImage.Information);
    }


    // TREEVIEW

    [ObservableProperty]
    private ObservableCollection<TreeNodeViewModel> rootNode = [];

    [ObservableProperty]
    private TreeNodeViewModel? selectedTreeNode;

    
    // LISTVIEW

    [ObservableProperty]
    private string selectedItemsText = "";

    [RelayCommand]
    private void Restore()
    {
        // TODO: Implement restore functionality
    }

    private static string ExtractDirectoryName(string relativeName)
    {
        // Extract directory name from path like "/folder1/folder2/" -> "folder2"
        var trimmed = relativeName.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
    }
}