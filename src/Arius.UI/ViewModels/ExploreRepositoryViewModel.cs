using Arius.Core.Facade;
using Arius.Core.Models;
using Arius.Core.Queries;
using Arius.Core.Services;
using Arius.UI.Services;
using Arius.UI.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
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
            var rootNode = new FolderViewModel { Name = "Root", RelativeDirectoryName = "", IsExpanded = true, IsSelected = true };
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

    public string WindowName => $"{App.Name}: {Repository.AccountName} - {Repository.ContainerName}";


    public RepositoryFacade Repository     { get; set; }
    public DirectoryInfo    LocalDirectory { get; set; }

    [ObservableProperty]
    private bool isLoading;

    private async Task LoadEntriesAsync()
    {
        if (SelectedFolder.IsLoaded)
            return;

        IsLoading = true;

        try
        {
            // Load local entries
            await ProcessEntries(FileService.GetEntriesAsync(LocalDirectory, SelectedFolder.RelativeDirectoryName));

            // Load database entries
            await ProcessEntries(Repository.QueryEntriesAsync(SelectedFolder.RelativeDirectoryName));

            async Task ProcessEntries(IAsyncEnumerable<IEntryQueryResult> entries)
            {
                // Create the necessary FolderViewModels and ItemViewModels for the given entries
                await foreach (var e in entries)
                {
                    var folderViewModel = GetOrCreateFolderViewModel(e.RelativeParentPath, e.DirectoryName);
                    var itemViewModel = GetOrCreateItemViewModel(folderViewModel, GetItemName(e.Name));

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
                    var parentFolder = foldersDict[nodeParentPath];
                    folderViewModel = new FolderViewModel
                    {
                        Name = directoryName,
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
                folderViewModel.TryGetItemViewModel(key,
                    out var itemViewModel,
                    () => new ItemViewModel(this) { Name = name }); // NOTE: we need this factory pattern, or otherwise the ItemViewModel is inserted with an empty name which screws up the sorting
                return itemViewModel;
            }

            void UpdateViewModel(ItemViewModel itemViewModel, IEntryQueryResult e)
            {
                if (e is FileService.GetLocalEntriesResult le)
                {
                    var filename = Path.Combine(LocalDirectory.FullName, le.RelativeParentPath, le.DirectoryName, le.Name);
                    var fib = FileSystemService.GetFileInfo(filename);
                    if (fib is PointerFileInfo pfi)
                        itemViewModel.PointerFileInfo = pfi;
                    else if (fib is BinaryFileInfo bfi)
                        itemViewModel.BinaryFileInfo = bfi;
                    else
                        throw new NotImplementedException();
                }
                else if (e is IPointerFileEntryQueryResult pfe)
                {
                    var path = Path.Combine(pfe.RelativeParentPath, pfe.DirectoryName, pfe.Name);
                    itemViewModel.PointerFileEntry = path;
                    itemViewModel.OriginalLength = pfe.OriginalLength;
                    itemViewModel.HydrationState = pfe.HydrationState;
                }
                else
                    throw new NotImplementedException();
            }

            SelectedFolder.IsLoaded = true;
        }
        finally
        {
            IsLoading = false;
        }
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
            Items   = new SortedObservableCollection<ItemViewModel>(new NaturalStringComparer());
        }

        [ObservableProperty]
        private string name;

        public string RelativeDirectoryName { get; init; }
        public bool   IsLoaded              { get; set; } = false;

        public ObservableCollection<FolderViewModel> Folders { get; }
        public SortedObservableCollection<ItemViewModel>   Items   { get; } // TODO ReadOnlyCollection public?

        public bool TryGetItemViewModel(string key, out ItemViewModel itemViewModel, Func<ItemViewModel> itemViewModelFactory)
        {
            if (!itemsDict.TryGetValue(key, out itemViewModel))
            {
                itemsDict.Add(key, itemViewModel = itemViewModelFactory());
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
        private readonly RepositoryExplorerViewModel parent;

        public ItemViewModel(RepositoryExplorerViewModel parent)
        {
            this.parent    = parent;
            HydrateCommand = new AsyncRelayCommand(OnHydrate, () => HydrationState == Core.Queries.HydrationState.NotHydrated);
            RestoreCommand = new AsyncRelayCommand(OnRestore, () => HydrationState != Core.Queries.HydrationState.NotHydrated); // explicitly leaving NeedsToBeChecked out here
        }

        [ObservableProperty]
        private string name;

        public BinaryFileInfo?  BinaryFileInfo   { get; set; }
        public PointerFileInfo? PointerFileInfo  { get; set; }
        public string?          PointerFileEntry { get; set; }
        public long             OriginalLength   { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(HydrateCommand))]
        [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
        private HydrationState? hydrationState;


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
                return HydrationState switch
                {
                    Arius.Core.Queries.HydrationState.Hydrated         => Brushes.Blue,
                    Arius.Core.Queries.HydrationState.NeedsToBeQueried => Brushes.Blue, // for chunked ones - graceful UI for now
                    Arius.Core.Queries.HydrationState.NotHydrated      => Brushes.LightBlue,
                    null => Brushes.Transparent,
                    _ => throw new ArgumentOutOfRangeException()
                };
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

                switch (HydrationState)
                {
                    case Core.Queries.HydrationState.Hydrated:
                        s.AppendLine("The remote file can be restored");
                        break;
                    case Core.Queries.HydrationState.NotHydrated:
                        s.AppendLine("The remote file needs to be hydrated first");
                        break;
                    case Core.Queries.HydrationState.NeedsToBeQueried:
                        s.AppendLine("The remote file is split up in parts. Not sure whether it can be downloaded.");
                        break;
                    case null:
                        s.AppendLine("The remote file does not exist");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                return s.ToString();
            }
        }

        public AsyncRelayCommand HydrateCommand { get; }
        private async Task OnHydrate()
        {
            // Hydration logic
        }

        public AsyncRelayCommand RestoreCommand { get; }
        private async Task OnRestore()
        {
            await parent.Repository.ExecuteRestoreCommandAsync(parent.LocalDirectory, pointerFileEntries: PointerFileEntry);
        }

        public override string ToString() => Name;
    }
}