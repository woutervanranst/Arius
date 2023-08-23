using Arius.Core.Facade;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

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
    public ExploreRepositoryViewModel(IMessenger messenger)
    {
        messenger.Register<PropertyChangedMessage<bool>>(this, HandlePropertyChange);

        // Set the selected folder to the root and kick off the loading process
        SelectedFolder = AddRootFolder(); //new FolderViewModel { Name = "Root", RelativePath = "", Parent = null };
    }

    private void HandlePropertyChange(object recipient, PropertyChangedMessage<bool> message)
    {
        switch (message.PropertyName)
        {
            case nameof(FolderViewModel.IsSelected):
                SelectedFolder = (FolderViewModel)message.Sender;
                break;

            case nameof(FolderViewModel.IsExpanded):
                // Handle IsExpanded change
                break;

            default:
                break;
        }
    }

    public string WindowName => $"Arius: {repository.AccountName} - {repository.ContainerName}";


    [ObservableProperty]
    private RepositoryFacade repository;

    private async Task LoadEntriesAsync()
    {
        if (SelectedFolder.IsLoaded)
            return;

        var x = await repository
            .GetEntriesAsync(SelectedFolder.RelativeDirectoryName)
            .ToListAsync();

        var y = await FileService
            .GetEntriesAsync(SelectedFolder.RelativeDirectoryName)
            .ToListAsync();

        await foreach (var e in repository
                           .GetEntriesAsync(SelectedFolder.RelativeDirectoryName))
        {
            var nodePath       = Path.Combine("root", e.RelativeParentPath, e.DirectoryName);
            if (!folders.TryGetValue(nodePath, out var folder))
            {
                var nodeParentPath = Path.Combine("root", e.RelativeParentPath);
                var parentFolder = folders[nodeParentPath];
                folders.Add(nodePath, folder = new FolderViewModel
                {
                    Name                  = e.DirectoryName,
                    NodePath              = nodePath,
                    RelativeDirectoryName = CombinePathSegments(e.RelativeParentPath, e.DirectoryName),
                    Parent                = parentFolder
                });

                parentFolder.Folders.Add(folder);
            }

            folder.Items.Add(new ItemViewModel { Name = e.Name });
        }

        SelectedFolder.IsLoaded = true;

        string CombinePathSegments(string segment1, string segment2)
        {
            return Path.Combine(segment1, segment2).Replace(Path.DirectorySeparatorChar, '/');
            //// Return the non-empty segment if one of them is empty or null
            //if (string.IsNullOrEmpty(segment1)) return segment2 ?? string.Empty;
            //if (string.IsNullOrEmpty(segment2)) return segment1;

            //// Both segments are non-empty, join with a forward slash
            //return $"{segment1}/{segment2}";
        }
    }

    private readonly Dictionary<string, FolderViewModel> folders = new();

    private FolderViewModel AddRootFolder()
    {
        var root = new FolderViewModel { Name = "Root", NodePath = "root", RelativeDirectoryName  = "", Parent = null };
        folders.Add("root", root);
        RootNode.Add(root);

        return root;
    }


    [ObservableProperty]
    private ObservableCollection<FolderViewModel> rootNode = new(); // this will really only contain one node but the TreeView binds to a collection

    public FolderViewModel SelectedFolder
    {
        get => selectedFolder;
        set
        {
            if (SetProperty(ref selectedFolder, value))
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(LoadEntriesAsync, DispatcherPriority.Background);
            }
        }
    }
    private FolderViewModel  selectedFolder;


    public partial class FolderViewModel : ObservableRecipient
    {
        public FolderViewModel()
        {
            Folders = new ObservableCollection<FolderViewModel>();
            Items   = new ObservableCollection<ItemViewModel>();
        }

        public string Name               { get; init; }
        //public string NodePath           => $"root\\{Path.Combine(RelativeParentPath, DirectoryName)}"; //{ get; init; } // TODO is nodepath just 'root' + relativepath?
        public string NodePath           { get; init; }
        //public string RelativeParentPath { get; init; }
        //public string DirectoryName      { get; init; }
        public string RelativeDirectoryName { get; init; }
        public bool   IsLoaded              { get; set; } = false;

        public FolderViewModel? Parent { get; set; }

        //public string FullPath
        //{
        //    get
        //    {
        //        var path   = Name;
        //        var parent = Parent;

        //        while (parent != null)
        //        {
        //            path   = $"{parent.Name}\\{path}";
        //            parent = parent.Parent;
        //        }

        //        return path;
        //    }
        //}

        public ObservableCollection<FolderViewModel> Folders { get; }
        public ObservableCollection<ItemViewModel>   Items   { get; }

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        private bool isSelected;

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        private bool isExpanded;

        public override string ToString() => NodePath;
    }

    public partial class ItemViewModel : ObservableObject
    {
        public string Name { get; set; }

        public override string ToString() => Name;
    }
}