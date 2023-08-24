using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Threading;
using Arius.Core.Facade;
using Arius.Core.Models;
using Arius.Core.Queries;
using Arius.Core.Services;
using Arius.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using WouterVanRanst.Utils.Extensions;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Arius.UI.ViewModels;

public partial class RepositoryExplorerViewModel : ObservableObject
{
    public RepositoryExplorerViewModel(IMessenger messenger)
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
    public RepositoryFacade Repository     { get; set; }
    public DirectoryInfo    LocalDirectory { get; set; }

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
        await ProcessEntries(FileService.GetEntriesAsync(LocalDirectory, SelectedFolder.RelativeDirectoryName));

        // Load database entries
        await ProcessEntries(Repository.GetEntriesAsync(SelectedFolder.RelativeDirectoryName));

        async Task ProcessEntries(IAsyncEnumerable<IGetEntriesResult> entries)
        {
            // Create the necessary FolderViewModels and ItemViewModels for the given entries
            await foreach (var e in entries)
            {
                var folderViewModel = GetOrCreateFolderViewModel(e.RelativeParentPath, e.DirectoryName);
                var itemViewModel   = GetOrCreateItemViewModel(folderViewModel, GetItemName(e.Name));

                // Set update the viewmodel with the entry
                UpdateViewModel(itemViewModel, e);
            }

            static string GetItemName(string name) => name.EndsWith(PointerFileInfo.Extension) 
                ? name.RemoveSuffix(".pointer.arius") 
                : name;
        }

        FolderViewModel GetOrCreateFolderViewModel(string relativeParentPath, string directoryName)
        {
            var key = Path.Combine(ROOT_NODEKEY, relativeParentPath, directoryName);
            if (!foldersDict.TryGetValue(key, out var folderViewModel))
            {
                // We need a new FolderViewModel
                var nodeParentPath = Path.Combine(ROOT_NODEKEY, relativeParentPath);
                var parentFolder   = foldersDict[nodeParentPath];
                folderViewModel = new FolderViewModel
                {
                    Name                  = directoryName,
                    RelativeDirectoryName = Path.Combine(relativeParentPath, directoryName)
                };
                foldersDict.Add(key, folderViewModel);
                parentFolder.Folders.Add(folderViewModel);
            }

            return folderViewModel;
        }

        ItemViewModel GetOrCreateItemViewModel(FolderViewModel folderViewModel, string name)
        {
            var key = Path.Combine(folderViewModel.RelativeDirectoryName, name);
            if (!folderViewModel.TryGetItemViewModel(key, out var itemViewModel))
            {
                // We need a new ItemViewModel
                itemViewModel.Name = name;
            }

            return itemViewModel;
        }

        void UpdateViewModel(ItemViewModel itemViewModel, IGetEntriesResult e)
        {
            if (e is FileService.GetLocalEntriesResult le)
            {
                var filename = Path.Combine(LocalDirectory.FullName, le.RelativeParentPath, le.DirectoryName, le.Name);
                var fib      = FileSystemService.GetFileInfo(filename);
                if (fib is PointerFileInfo pfi)
                    itemViewModel.PointerFileInfo = pfi;
                else if (fib is BinaryFileInfo bfi)
                    itemViewModel.BinaryFileInfo = bfi;
                else
                    throw new NotImplementedException();
            }
            else if (e is IGetPointerFileEntriesResult pfe)
            {
                var path = Path.Combine(pfe.RelativeParentPath, pfe.DirectoryName, pfe.Name);
                itemViewModel.PointerFileEntry = path;
                itemViewModel.OriginalLength   = pfe.OriginalLength;
            }
            else
                throw new NotImplementedException();
        }


        SelectedFolder.IsLoaded = true;
    }

    private const string ROOT_NODEKEY = "root";

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
    private FolderViewModel selectedFolder;


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

        public BinaryFileInfo?  BinaryFileInfo   { get; set; }
        public PointerFileInfo? PointerFileInfo  { get; set; }
        public string?          PointerFileEntry { get; set; }
        public long             OriginalLength   { get; set; }

        /// <summary>
        /// Get the string representing the state of the item for the bollekes (eg. pointer present, blob present, ...)
        /// </summary>
        public string ItemState
        {
            get
            {
                var itemState = new StringBuilder();

                itemState.Append(BinaryFileInfo is not null ? 'Y' : 'N');
                itemState.Append(PointerFileInfo is not null ? 'Y' : 'N');
                itemState.Append(PointerFileEntry is not null ? 'Y' : 'N');
                itemState.Append('A');
                //itemState.Append(Manifest is not null ? 'A' : throw new NotImplementedException());

                return itemState.ToString();
            }
        }

        public Brush PointerFileStateColor
        {
            get
            {
                if (PointerFileInfo is not null)
                    return Brushes.Black;
                else
                    return Brushes.Transparent;
            }
        }

        public Brush BinaryFileStateColor
        {
            get
            {
                if (BinaryFileInfo is not null)
                    return Brushes.Blue;
                else
                    return Brushes.Transparent;
            }
        }

        public Brush PointerFileEntryStateColor
        {
            get
            {
                if (PointerFileEntry is not null)
                    return Brushes.Black;
                else
                    return Brushes.Transparent;
            }
        }

        public Brush ChunkStateColor
        {
            get
            {
                return Brushes.LightBlue;
            }
        }

        public string StateTooltip
        {
            get
            {
                var s = new StringBuilder();

                if (PointerFileInfo is not null)
                    s.AppendLine("The local PointerFile exists");
                else
                    s.AppendLine("The local PointerFile does not exist");

                if (BinaryFileInfo is not null)
                    s.AppendLine("The local BinaryFile exists");
                else
                    s.AppendLine("The local BinaryFile does not exist");

                if (PointerFileEntry is not null)
                    s.AppendLine("The remote entry exists");
                else
                    s.AppendLine("The remote entry not exist");

                if (true)
                    s.AppendLine("HYDRATED");

                return s.ToString();
            }
        }

        public override string ToString() => Name;
    }
}