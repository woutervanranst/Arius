using Arius.Core.Facade;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Threading;
using Arius.Core.Models;
using Arius.Core.Services;
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

//#if DEBUG
//        var x = await Repository
//            .GetEntriesAsync(SelectedFolder.RelativeDirectoryName)
//            .ToListAsync();

//        var y = FileService
//            .GetEntries(new DirectoryInfo("C:\\Users\\woute\\Documents\\AriusTest"), 
//                SelectedFolder.RelativeDirectoryName)
//            .Where(e => e.Name.EndsWith(".pointer.arius"))
//            .ToList();

//        var z  = x.Except(y);
//        var zz = y.Except(x);
//        if (z.Any() || zz.Any())
//        {

//        }
//#endif

        // Load local entries
        await ProcessEntries(FileService.GetEntriesAsync(LocalDirectory, SelectedFolder.RelativeDirectoryName),
                     CombineLocalFilePath,
                     SetLocalFilePaths);

        // Load database entries
        await ProcessEntries(Repository.GetEntriesAsync(SelectedFolder.RelativeDirectoryName),
                             CombinePointerFileEntryPath,
                             SetPointerFileEntry);

        async Task ProcessEntries(IAsyncEnumerable<(string RelativeParentPath, string DirectoryName, string Name)> entries,
                                  Func<string, string, string, string, string> pathCombiner,
                                  Action<ItemViewModel, string> setValue)
        {
            await foreach (var e in entries)
            {
                var folderViewModel = GetOrCreateFolderViewModel(e.RelativeParentPath, e.DirectoryName);
                var itemViewModel = GetOrCreateItemViewModel(folderViewModel, GetItemName(e.Name));

                // Get the string to the PointerFile, BinaryFile or PointerFileEntry
                var value = pathCombiner(LocalDirectory.FullName, e.RelativeParentPath, e.DirectoryName, e.Name);
                setValue(itemViewModel, value);
            }

            static string GetItemName(string name)
            {
                if (name.EndsWith(".pointer.arius")) // todo get this from PointerFile.Extension
                    return name.RemoveSuffix(".pointer.arius");
                else
                    return name;
            }
        }

        FolderViewModel GetOrCreateFolderViewModel(string relativeParentPath, string directoryName)
        {
            var nodePath = CombinePathSegments(ROOT_NODEKEY, relativeParentPath, directoryName);
            if (!foldersDict.TryGetValue(nodePath, out var folderViewModel))
            {
                var nodeParentPath = CombinePathSegments(ROOT_NODEKEY, relativeParentPath);
                var parentFolder = foldersDict[nodeParentPath];
                folderViewModel = new FolderViewModel
                {
                    Name = directoryName,
                    RelativeDirectoryName = CombinePathSegments(relativeParentPath, directoryName)
                };
                foldersDict.Add(nodePath, folderViewModel);
                parentFolder.Folders.Add(folderViewModel);
            }
            return folderViewModel;
        }

        ItemViewModel GetOrCreateItemViewModel(FolderViewModel folderViewModel, string name)
        {
            if (!folderViewModel.TryGetItemViewModel(CombinePathSegments(folderViewModel.RelativeDirectoryName, name), out var itemViewModel))
            {
                itemViewModel.Name = name;
            }
            return itemViewModel;
        }

        void SetLocalFilePaths(ItemViewModel itemViewModel, string filename)
        {
            var fib = FileSystemService.GetFileInfo(filename);
            if (fib is PointerFileInfo pfi)
                itemViewModel.PointerFileInfo = pfi;
            else if (fib is BinaryFileInfo bfi)
                itemViewModel.BinaryFileInfo = bfi;
            else
                throw new NotImplementedException();
        }
        void SetPointerFileEntry(ItemViewModel itemViewModel, string path)
        {
            itemViewModel.PointerFileEntry = path;
        }

        static string CombineLocalFilePath(string root, string relativeParentPath, string directoryName, string name)     => Path.Combine(root, relativeParentPath, directoryName, name);
        static string CombinePointerFileEntryPath(string _, string relativeParentPath, string directoryName, string name) => Path.Combine(relativeParentPath, directoryName, name);

        static string CombinePathSegments(params string[] segments) => Path.Combine(segments).Replace(Path.DirectorySeparatorChar, '/');


        SelectedFolder.IsLoaded = true;
    }

    private const string ROOT_NODEKEY = "root";



    //private bool TryGetFolderViewModel(string key, out FolderViewModel folderViewModel)
    //{
    //    if (!foldersDict.TryGetValue(key, out folderViewModel))
    //    {
    //        foldersDict.Add(key, folderViewModel = new FolderViewModel());
    //        RootNode.Add(folderViewModel);
    //        return false;
    //    }

    //    return true;
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

        public BinaryFileInfo BinaryFileInfo   { get; set; }
        public PointerFileInfo PointerFileInfo  { get; set; }
        public string PointerFileEntry { get; set; }

        public override string ToString() => Name;
    }
}