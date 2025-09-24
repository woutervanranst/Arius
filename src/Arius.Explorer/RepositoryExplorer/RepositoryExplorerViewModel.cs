using Arius.Core.Features.Commands.Restore;
using Arius.Core.Features.Queries.PointerFileEntries;
using Arius.Explorer.Settings;
using Arius.Explorer.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Humanizer;
using Mediator;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text;
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
            OnPropertyChanged(nameof(SelectedItemsText));
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
                OnPropertyChanged(nameof(SelectedItemsText));
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    private async void OnNodeSelected(TreeNodeViewModel selectedNode)
    {
        // Clear selection when switching nodes
        SelectedFiles.Clear();

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
                AccountName   = Repository.AccountName,
                AccountKey    = Repository.AccountKey,
                ContainerName = Repository.ContainerName,
                Passphrase    = Repository.Passphrase,
                LocalPath     = new DirectoryInfo(Repository.LocalDirectoryPath),
                Prefix        = node.Prefix
            };

            // Initialize collections for streaming updates
            node.Folders = [];
            node.Items = [];

            // Update the selected tree node reference for ListView binding immediately
            SelectedTreeNode = node;
            ArchiveStatistics = "Loading...";

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
                            OnPropertyChanged(nameof(SelectedItemsText));

                            break;
                    }
                });
            }

            // Final count update (in case there were only directories)
            //Application.Current.Dispatcher.Invoke(() =>
            //{
            OnPropertyChanged(nameof(SelectedItemsText));
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
    private ObservableCollection<FileItemViewModel> selectedFiles = [];

    [RelayCommand]
    private void ItemSelectionChanged(FileItemViewModel item)
    {
        if (item.IsSelected)
        {
            if (!SelectedFiles.Contains(item))
            {
                SelectedFiles.Add(item);
            }
        }
        else
        {
            SelectedFiles.Remove(item);
        }
        OnPropertyChanged(nameof(SelectedItemsText));
    }

    public string SelectedItemsText
    {
        get
        {
            var selectedCount = SelectedFiles.Count;
            var totalCount    = SelectedTreeNode?.Items.Count ?? 0;
            var totalSize     = SelectedTreeNode?.Items.Sum(item => item.OriginalLength) ?? 0;

            if (selectedCount == 0)
            {
                if (totalCount == 0)
                {
                    return "0 item(s)";
                }
                else
                {

                    return totalCount > 0 ? $"{totalCount} item(s), {totalSize.Bytes().Humanize()}" : "";
                }
            }
            else
            {
                var totalSelectedSize = SelectedFiles.Sum(item => item.OriginalLength);

                return $"{selectedCount} of {totalCount} item(s) selected, {totalSelectedSize.Bytes().Humanize()} of {totalSize.Bytes().Humanize()}";
            }
        }
    }

    // RESTORE

    [RelayCommand]
    private async Task Restore()
    {
        // Validate prerequisites
        if (Repository == null || !SelectedFiles.Any())
            return;

        // Show confirmation dialog
        var msg = new StringBuilder();

        var itemsToHydrate = SelectedFiles.Where(item => item.File.Hydrated == false);
        if (itemsToHydrate.Any())
            msg.AppendLine($"This will start hydration on {itemsToHydrate.Count()} item(s) ({itemsToHydrate.Sum(item => item.OriginalLength).Bytes().Humanize()}). This may incur a significant cost.");

        var itemsToRestore = SelectedFiles.Where(item => item.File.Hydrated == true);
        msg.AppendLine($"This will download {itemsToRestore.Count()} item(s) ({itemsToRestore.Sum(item => item.OriginalLength).Bytes().Humanize()}).");
        msg.AppendLine();
        msg.AppendLine("Proceed?");

        if (MessageBox.Show(msg.ToString(), App.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
            return;

        // Extract the relative paths from selected items
        var targets = SelectedFiles
            .Select(f => $".{f.File.PointerFileEntry ?? f.File.BinaryFileName ?? f.File.PointerFileName}")
            .Where(path => !string.IsNullOrEmpty(path))
            .ToArray();

        if (!targets.Any())
            return;

        // Create the RestoreCommand
        var command = new RestoreCommand
        {
            AccountName     = Repository.AccountName,
            AccountKey      = Repository.AccountKey,
            ContainerName   = Repository.ContainerName,
            Passphrase      = Repository.Passphrase,
            LocalRoot       = new DirectoryInfo(Repository.LocalDirectoryPath),
            Targets         = targets,
            Download        = true,
            IncludePointers = false
        };

        // Execute the restore
        try
        {
            IsLoading = true;
            var result = await mediator.Send(command);

            // Refresh the view after restore
            if (SelectedTreeNode != null)
                await LoadNodeContentAsync(SelectedTreeNode);
        }
        catch (Exception ex)
        {
            // Handle error (optionally show message)
            MessageBox.Show($"Restore failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string ExtractDirectoryName(string relativeName) // TODO move this logic to the TreeNodeViewModel, just like FileItemViewModel
    {
        // Extract directory name from path like "/folder1/folder2/" -> "folder2"
        var trimmed = relativeName.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
    }
}