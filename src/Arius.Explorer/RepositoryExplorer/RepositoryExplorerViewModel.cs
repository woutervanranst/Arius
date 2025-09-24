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
        Repository = recentRepositoryManager.GetMostRecent();  // this will trigger OnRepositoryChanged
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
    

    // MENUS

    //      File > Open...

    [RelayCommand] 
    private void ChooseRepository()
    {
        // Show dialog and handle result
        var openedRepository = dialogService.ShowChooseRepositoryDialog(Repository);
        if (openedRepository != null)
        {
            OpenRepository(openedRepository);
        }
    }

    //      File > Recent > [list]

    [ObservableProperty]
    private ObservableCollection<RepositoryOptions> recentRepositories = [];

    [RelayCommand]
    private void OpenRepository(RepositoryOptions repository)
    {
        // Use the new service to update recent repositories
        recentRepositoryManager.TouchOrAdd(repository);

        Repository = repository; // this will trigger OnRepositoryChanged
    }

    // -- REPOSITORY

    [ObservableProperty]
    private RepositoryOptions? repository;

    partial void OnRepositoryChanged(RepositoryOptions? value)
    {
        WindowName = value == null
            ? $"{App.Name} - No Repository"
            : $"{App.Name}: {value}";

        if (value != null)
        {
            // Fire and forget - load repository data asynchronously
            _ = Task.Run(async () => await LoadRepositoryAsync());
        }
        else
        {
            // Clear UI when no repository
            RootNode          = [];
            SelectedFolder    = new FolderViewModel();
            SelectedItemsText = "";
        }


        ArchiveStatistics = $"Repository: {repository.LocalDirectoryPath}";

        //if (r is not null)
        //    OpenRepository(r);
        //if (Repository != null)
        //{
        //    ArchiveStatistics = $"Repository: {Repository.LocalDirectoryPath}";
        //}
        //else
        //{
        //    ArchiveStatistics = "";
        //    RootNode = [];
        //    SelectedFolder = new FolderViewModel();
        //    SelectedItemsText = "";
        //}
    }

    private async Task LoadRepositoryAsync()
    {
        if (Repository == null)
            return;

        IsLoading = true;
        try
        {
            // Create root node
            var rootNode = new TreeNodeViewModel("/", OnNodeSelected)
            {
                Name = "Root"
            };

            RootNode = [rootNode];

            // Load initial content for root
            await LoadNodeContentAsync(rootNode);
        }
        finally
        {
            IsLoading = false;
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
    private FolderViewModel? selectedFolder;

    
    // LISTVIEW

    [ObservableProperty]
    private string selectedItemsText = "";

    [RelayCommand]
    private void Restore()
    {
        // TODO: Implement restore functionality
    }

    // HELPER METHODS



    private async Task LoadNodeContentAsync(TreeNodeViewModel node)
    {
        try
        {
            if (Repository == null)
                return;

            var query = new PointerFileEntriesQuery
            {
                AccountName   = Repository.AccountName,
                AccountKey    = Repository.AccountKey,
                ContainerName = Repository.ContainerName,
                Passphrase    = Repository.Passphrase,
                LocalPath     = new DirectoryInfo(Repository.LocalDirectoryPath),
                Prefix        = node.Prefix
            };

            var results     = mediator.CreateStream(query);
            var directories = new List<TreeNodeViewModel>();
            var files       = new List<FileItemViewModel>();

            await foreach (var result in results)
            {
                switch (result)
                {
                    case PointerFileEntriesQueryDirectoryResult directoryResult:
                        var dirName = ExtractDirectoryName(directoryResult.RelativeName);
                        var childNode = new TreeNodeViewModel(directoryResult.RelativeName, OnNodeSelected)
                        {
                            Name = dirName
                        };
                        directories.Add(childNode);
                        break;

                    case PointerFileEntriesQueryFileResult fileResult:
                        var fileName = ExtractFileName(fileResult);
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            var fileItem = new FileItemViewModel(fileName, fileResult.OriginalSize);
                            files.Add(fileItem);
                        }

                        break;
                }
            }

            // Update UI on main thread
            node.Folders = new ObservableCollection<TreeNodeViewModel>(directories);

            if (SelectedFolder == null)
                SelectedFolder = new FolderViewModel();

            SelectedFolder.Items = new ObservableCollection<FileItemViewModel>(files);
            SelectedItemsText    = $"{files.Count} items";
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            //throw;
        }
    }

    private static string ExtractDirectoryName(string relativeName)
    {
        // Extract directory name from path like "/folder1/folder2/" -> "folder2"
        var trimmed = relativeName.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
    }

    private static string ExtractFileName(PointerFileEntriesQueryFileResult file)
    {
        // Extract file name from path like "/folder1/file.txt" -> "file.txt"
        var n = file.BinaryFileName ?? file.PointerFileEntry;

        if (string.IsNullOrEmpty(n))
            return string.Empty;

        var lastSlash = n.LastIndexOf('/');
        return lastSlash >= 0 ? n[(lastSlash + 1)..] : n;
    }

    private async void OnNodeSelected(TreeNodeViewModel selectedNode)
    {
        // Load the content for the selected node
        await LoadNodeContentAsync(selectedNode);
    }
}