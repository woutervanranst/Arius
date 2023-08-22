using Arius.Core.Facade;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace Arius.UI;

/// <summary>
/// Interaction logic for RepositoryExplorerWindow.xaml
/// </summary>
public partial class RepositoryExplorerWindow : Window
{
    public RepositoryExplorerWindow()
    {
        InitializeComponent();
    }
}

public partial class ExploreRepositoryViewModel : ObservableObject
{
    public ExploreRepositoryViewModel()
    {
        Folders = new ObservableCollection<DirectoryItem>();
        FileList       = new ObservableCollection<FileItem>();
    }

    //public IAsyncRelayCommand LoadEntriesCommand => new AsyncRelayCommand(LoadEntriesAsync);

    public string WindowName => $"Arius: {repository.AccountName} - {repository.ContainerName}";


    public RepositoryFacade Repository
    {
        get => repository;
        set
        {
            if (SetProperty(ref repository, value))
            {
                LoadEntriesAsync();
            }
        }
    }
    private RepositoryFacade repository;

    private async Task LoadEntriesAsync()
    {
        var prefix = SelectedFolder?.FullPath ?? "";

        var x      = await repository.GetEntriesAsync(prefix).ToListAsync();

        Folders.Clear();

        await foreach (var entry in repository.GetEntriesAsync(prefix))
        {
            Folders.Add(DirectoryItem.FromEntry(entry.RelativePath));

            //// If it's root, then build the directory tree
            //if (string.IsNullOrEmpty(path) && entry.Contains("\\"))
            //{
            //    // Note: Modify this based on how you're building your directory tree.
            //    var directoryItem = BuildDirectoryItem(entry);
            //    if (directoryItem != null)
            //        DirectoryItems.Add(directoryItem);
            //}
            //else
            //{
            //    FileList.Add(new FileItem { Name = entry });
            //}
        }
    }

    //private FileItem BuildDirectoryItem(string entry)
    //{
    //    // This method should break down the entry into directories and subdirectories.
    //    // This is a basic implementation. You'd likely want to handle nested directories properly.
    //    var directories = entry.Split('\\');
    //    if (directories.Length > 0)
    //    {
    //        return new FileItem { Name = directories[0] };
    //    }

    //    return null;
    //}




    [ObservableProperty]
    private ObservableCollection<DirectoryItem> folders;

    [ObservableProperty] 
    private DirectoryItem selectedFolder;


    [ObservableProperty]
    private ObservableCollection<FileItem> fileList;




    //public ICommand DirectorySelectedCommand => new RelayCommand<DirectoryItem>(OnDirectorySelected);



    //private ObservableCollection<DirectoryItem> BuildDirectoryTree(IEnumerable<string> directories)
    //{
    //    // Convert the list of directories to a hierarchical ObservableCollection of DirectoryItems
    //    // (This can be a recursive operation)
    //}

    //private void OnDirectorySelected(DirectoryItem selectedDirectory)
    //{
    //    // Update the fileList based on the selected directory
    //}

    //public class DirectoryItem
    //{
    //    public string                              Name           { get; set; }
    //    public ObservableCollection<DirectoryItem> SubDirectories { get; set; }
    //}

    public class DirectoryItem : FileItem
    {
        public static DirectoryItem FromEntry(string entry)
        {
            var parts = entry.Split('\\');

            var currentDir = new DirectoryItem { Name = parts[0] };

            for (int i = 1; i < parts.Length - 1; i++)
            {
                var newDir = new DirectoryItem { Name = parts[i], Parent = currentDir };
                currentDir.Subdirectories.Add(newDir);
                currentDir = newDir;
            }

            if (parts.Length > 1)
            {
                var file = new FileItem { Name = parts[^1] };
                currentDir.Files.Add(file);
            }

            return currentDir;
        }


        public DirectoryItem()
        {
            Subdirectories = new ObservableCollection<DirectoryItem>();
            Files          = new ObservableCollection<FileItem>();
        }

        public DirectoryItem Parent { get; set; }

        public string FullPath
        {
            get
            {
                var path   = Name;
                var parent = Parent;

                while (parent != null)
                {
                    path   = $"{parent.Name}\\{path}";
                    parent = parent.Parent;
                }

                return path;
            }
        }

        public ObservableCollection<DirectoryItem> Subdirectories { get; }
        public ObservableCollection<FileItem>      Files          { get; }
    }


    public class FileItem
    {
        public string Name { get; set; }
    }



}