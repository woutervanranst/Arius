using Arius.Core.Facade;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Threading;
using WouterVanRanst.Utils.Extensions;

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
            foldersDict.Add(ROOT_NODEKEY, rootNode);
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

    public string WindowName => $"Arius: {Repository.AccountName} - {Repository.ContainerName}";


    //[ObservableProperty]
    //private RepositoryFacade repository;
    public RepositoryFacade Repository { get; set; }
    public DirectoryInfo    LocalDirectory       { get; set; }

    private async Task LoadEntriesAsync()
    {
        if (SelectedFolder.IsLoaded)
            return;

#if DEBUG
        var x = await Repository
            .GetEntriesAsync(SelectedFolder.RelativeDirectoryName)
            .ToListAsync();

        var y = FileService
            .GetEntries(new DirectoryInfo("C:\\Users\\woute\\Documents\\AriusTest"), 
                SelectedFolder.RelativeDirectoryName)
            .Where(e => e.Name.EndsWith(".pointer.arius"))
            .ToList();

        var z  = x.Except(y);
        var zz = y.Except(x);
        if (z.Any() || zz.Any())
        {

        }
#endif
        // Load the Local entries
        foreach (var e in FileService
                     .GetEntries(LocalDirectory, SelectedFolder.RelativeDirectoryName))
        {
            // Get the node where this entry belongs to
            var nodePath = CombinePathSegments(ROOT_NODEKEY, e.RelativeParentPath, e.DirectoryName);
            if (!foldersDict.TryGetValue(nodePath, out var folder))
            {
                var nodeParentPath = CombinePathSegments(ROOT_NODEKEY, e.RelativeParentPath);
                var parentFolder   = foldersDict[nodeParentPath];
                foldersDict.Add(nodePath, folder = new FolderViewModel
                {
                    Name                  = e.DirectoryName,
                    RelativeDirectoryName = CombinePathSegments(e.RelativeParentPath, e.DirectoryName)
                });

                parentFolder.Folders.Add(folder);
            }

            var name = GetItemName(e.Name);
            if (!folder.TryGetItemViewModel(CombinePathSegments(folder.RelativeDirectoryName, name), out var itemViewModel))
                itemViewModel.Name = name;

            if (e.Name.EndsWith(".pointer.arius")) // todo get this from PointerFile.Extension
                itemViewModel.PointerFilePath = Path.Combine(LocalDirectory.FullName, e.RelativeParentPath, e.DirectoryName, e.Name);
            else
                itemViewModel.BinaryFilePath = Path.Combine(LocalDirectory.FullName, e.RelativeParentPath, e.DirectoryName, e.Name);

        }




        // Load the database entries
        await foreach (var e in Repository
                           .GetEntriesAsync(SelectedFolder.RelativeDirectoryName))
        {
            // Get the node where this entry belongs to
            var nodePath = CombinePathSegments(ROOT_NODEKEY, e.RelativeParentPath, e.DirectoryName);
            if (!foldersDict.TryGetValue(nodePath, out var folder))
            {
                // The node does not yet exist - create it
                var nodeParentPath = CombinePathSegments(ROOT_NODEKEY, e.RelativeParentPath);
                var parentFolder   = foldersDict[nodeParentPath];
                foldersDict.Add(nodePath, folder = new FolderViewModel
                {
                    Name                  = e.DirectoryName,
                    RelativeDirectoryName = CombinePathSegments(e.RelativeParentPath, e.DirectoryName),
                });

                parentFolder.Folders.Add(folder);
            }

            var name = e.Name.RemoveSuffix(".pointer.arius");
            if (!folder.TryGetItemViewModel(CombinePathSegments(folder.RelativeDirectoryName, name), out var itemViewModel))
                itemViewModel.Name = name;

            itemViewModel.PointerFileEntry = Path.Combine(e.RelativeParentPath, e.DirectoryName, e.Name);
        }

        SelectedFolder.IsLoaded = true;


        static string GetItemName(string name)
        {
            if (name.EndsWith(".pointer.arius")) // todo get this from PointerFile.Extension
                return name.RemoveSuffix(".pointer.arius");
            else
                return name;
        }
    }

    private const string ROOT_NODEKEY = "root";

    private static string CombinePathSegments(params string[] segments)
    {
        return Path.Combine(segments).Replace(Path.DirectorySeparatorChar, '/');
    }

    //private bool TryAddFolderViewModel(string key, out FolderViewModel folderViewModel)
    //{
    //    if (!foldersDict.TryGetValue(key, out folderViewModel))
    //    {
    //        foldersDict.Add(key, folderViewModel = new FolderViewModel());
    //        RootNode.Add(folderViewModel);
    //    }

    //    //if (!itemsDict.TryGetValue(key, out var itemViewModel))
    //    //{
    //    //    itemsDict.Add(key, itemViewModel = new ItemViewModel { Name = name });
    //    //    Items.Add(itemViewModel);
    //    //}

    //    //return itemViewModel;
    //}

    private readonly Dictionary<string, FolderViewModel> foldersDict = new();

    


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
        public ObservableCollection<ItemViewModel>   Items   { get; } // TODO ReadOnlyCollection public?

        public bool TryGetItemViewModel(string key, out ItemViewModel itemViewModel)
        {
            if (!itemsDict.TryGetValue(key, out itemViewModel))
            {
                itemsDict.Add(key, itemViewModel = new ItemViewModel( ));
                Items.Add(itemViewModel);
                return false;
            }

            return true;
        }
        private readonly Dictionary<string, ItemViewModel> itemsDict = new();

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

        public string BinaryFilePath   { get; set; }
        public string PointerFilePath  { get; set; }
        public string PointerFileEntry { get; set; }

        public override string ToString() => Name;
    }
}