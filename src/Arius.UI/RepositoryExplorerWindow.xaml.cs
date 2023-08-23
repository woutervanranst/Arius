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
        SelectedFolder = GetRootNode();

        FolderViewModel GetRootNode()
        {
            var rootNode = new FolderViewModel { Name = "Root", RelativeDirectoryName = "" };
            folders.Add(ROOT_NODEKEY, rootNode);
            RootNode.Add(rootNode);

            return rootNode;
        }
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
            // Get the node where this entry belongs to
            var nodePath = CombinePathSegments(ROOT_NODEKEY, e.RelativeParentPath, e.DirectoryName);
            if (!folders.TryGetValue(nodePath, out var folder))
            {
                // The node does not yet exist - create it
                var nodeParentPath = CombinePathSegments(ROOT_NODEKEY, e.RelativeParentPath);
                var parentFolder   = folders[nodeParentPath];
                folders.Add(nodePath, folder = new FolderViewModel
                {
                    Name                  = e.DirectoryName,
                    RelativeDirectoryName = CombinePathSegments(e.RelativeParentPath, e.DirectoryName),
                });

                parentFolder.Folders.Add(folder);
            }

            folder.Items.Add(new ItemViewModel { Name = e.Name });
        }

        SelectedFolder.IsLoaded = true;
    }

    private const string ROOT_NODEKEY = "root";

    private static string CombinePathSegments(params string[] segments)
    {
        return Path.Combine(segments).Replace(Path.DirectorySeparatorChar, '/');
    }

    private readonly Dictionary<string, FolderViewModel> folders = new();

    


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

        public string Name                  { get; init; }
        public string RelativeDirectoryName { get; init; }
        public bool   IsLoaded              { get; set; } = false;

        public ObservableCollection<FolderViewModel> Folders { get; }
        public ObservableCollection<ItemViewModel>   Items   { get; }

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        private bool isSelected;

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        private bool isExpanded;

        public override string ToString() => RelativeDirectoryName;
    }

    public partial class ItemViewModel : ObservableObject
    {
        public string Name { get; set; }

        public override string ToString() => Name;
    }
}