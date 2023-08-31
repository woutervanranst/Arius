using Arius.Core.Facade;
using Arius.Core.Models;
using Arius.Core.Queries;
using Arius.Core.Services;
using Arius.UI.Services;
using Arius.UI.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Threading;
using WouterVanRanst.Utils.Extensions;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Arius.UI.ViewModels;

internal partial class RepositoryExplorerViewModel : ObservableRecipient
{
    private readonly ApplicationSettings settings;

    public RepositoryExplorerViewModel(ApplicationSettings settings)
    {
        this.settings = settings;
        Messenger.Register<PropertyChangedMessage<bool>>(this, HandlePropertyChange);

        OpenRepositoryCommand = new RelayCommand(OnOpenRepository);
        RestoreCommand        = new AsyncRelayCommand(OnRestoreAsync, CanRestore);
        AboutCommand          = new RelayCommand(OnAbout);
    }

    public RepositoryFacade Repository
    {
        get => repository;
        set
        {
            if (SetProperty(ref repository, value))
            {
                // Set the selected folder to the root and kick off the loading process
                SelectedFolder = GetRootNode();
                OnPropertyChanged(nameof(WindowName));
            }
        }
    }
    private RepositoryFacade repository;
    
    public DirectoryInfo LocalDirectory { get; set; }

    public string WindowName
    {
        get
        {
            if (Repository is null)
                return $"{App.Name} - No repository";
            else
                return $"{App.Name}: {Repository.AccountName} - {Repository.ContainerName}";
        }
    }

    [ObservableProperty]
    private bool isLoading;

    private void HandlePropertyChange(object recipient, PropertyChangedMessage<bool> message)
    {
        if (message.Sender is FolderViewModel folderViewModel)
        {
            switch (message.PropertyName)
            {
                case nameof(FolderViewModel.IsSelected):
                    SelectedFolder = folderViewModel;
                    break;
                case nameof(FolderViewModel.IsExpanded):
                    // Handle IsExpanded change
                    break;
                default:
                    break;
            }
        }
        else if (message.Sender is ItemViewModel itemViewModel)
        {
            switch (message.PropertyName)
            {
                case nameof(ItemViewModel.IsSelected):
                    if (message.NewValue)
                        SelectedItems.Add(itemViewModel);
                    else
                        SelectedItems.Remove(itemViewModel);

                    OnPropertyChanged(nameof(SelectedItemsText));
                    RestoreCommand.NotifyCanExecuteChanged();
                    break;
            }
        }
    }


    // Folders Treeview
    private const string ROOT_NODEKEY = "root";

    private FolderViewModel GetRootNode()
    {
        RootNode.Clear();
        foldersDict.Clear();

        var rootNode = new FolderViewModel { Name = "Root", RelativeDirectoryName = "", IsExpanded = true, IsSelected = true };
        foldersDict.Add(ROOT_NODEKEY, rootNode);
        RootNode.Add(rootNode);

        return rootNode;
    }

    [ObservableProperty]
    private ObservableCollection<FolderViewModel> rootNode = new(); // this will really only contain one node but the TreeView binds to a collection
    private readonly Dictionary<string, FolderViewModel> foldersDict = new(); // a lookup dictionary with the folder's relative path as key

    public FolderViewModel SelectedFolder
    {
        get => selectedFolder;
        set
        {
            if (SetProperty(ref selectedFolder, value))
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(LoadEntriesAsync, DispatcherPriority.Background);
                System.Windows.Application.Current.Dispatcher.InvokeAsync(LoadArchiveProperties, DispatcherPriority.Background);
            }
        }
    }
    private FolderViewModel selectedFolder;

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
                    () => new ItemViewModel() { Name = name }); // NOTE: we need this factory pattern, or otherwise the ItemViewModel is inserted with an empty name which screws up the sorting
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
                    {
                        itemViewModel.BinaryFileInfo = bfi;
                        itemViewModel.OriginalLength = bfi.Length;
                    }
                    else
                        throw new NotImplementedException();
                }
                else if (e is IPointerFileEntryQueryResult pfe)
                {
                    var relativeName = Path.Combine(pfe.RelativeParentPath, pfe.DirectoryName, pfe.Name);
                    itemViewModel.PointerFileEntryRelativeName = relativeName;
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

    private async Task LoadArchiveProperties()
    {
        var s = await Repository.QueryRepositoryStatisticsAsync();
        ArchiveStatistics = $"Total size: {s.TotalSize.GetBytesReadable()} in {s.TotalFiles} file(s) in {s.TotalChunks} unique part(s)";
    }

    // Item ListView
    [ObservableProperty]
    private ObservableCollection<ItemViewModel> selectedItems = new();

    public string SelectedItemsText => $"{SelectedItems.Count} item(s) selected, {SelectedItems.Sum(item => item.OriginalLength).GetBytesReadable(0)}";

    [ObservableProperty]
    public string archiveStatistics;


    // Commands
    public IRelayCommand OpenRepositoryCommand { get; }
    private void OnOpenRepository()
    {
        Messenger.Send(new ChooseRepositoryMessage
        {
            Sender         = this,
            LocalDirectory = this.LocalDirectory,
            AccountName    = Repository.AccountName,
            AccountKey     = Repository.AccountKey,  // TODO not ok
            ContainerName  = Repository.ContainerName,
            Passphrase     = Repository.Passphrase // TODO not ok
        });
    }


    public IRelayCommand RestoreCommand { get; }
    private async Task OnRestoreAsync()
    {
        var msg = new StringBuilder();

        var itemsToHydrate = SelectedItems.Where(item => item.HydrationState != HydrationState.Hydrated);
        if (itemsToHydrate.Any())
            msg.AppendLine($"This will start hydration on {itemsToHydrate.Count()} item(s) ({itemsToHydrate.Sum(item => item.OriginalLength).GetBytesReadable(0)}). This may incur a significant cost.");

        var itemsToRestore  = SelectedItems.Where(item => item.HydrationState == HydrationState.Hydrated);
        msg.AppendLine($"This will download {itemsToRestore.Count()} item(s) ({itemsToRestore.Sum(item => item.OriginalLength).GetBytesReadable(0)}).");
        msg.AppendLine();
        msg.AppendLine("Proceed?");

        if (MessageBox.Show(msg.ToString(), App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
            return;

        var relativeNames = SelectedItems.Select(item => item.PointerFileEntryRelativeName).ToArray();

        var r = await Repository.ExecuteRestoreCommandAsync(LocalDirectory,
            relativeNames: relativeNames,
            download: true,
            keepPointers: false);

        if (r == 0)
        {
            msg = new StringBuilder();

            if (itemsToHydrate.Any())
                msg.AppendLine("Hydration started. These files will be ready for download in ~18 hours.");

            msg.AppendLine("Files downloaded!");

            MessageBox.Show(msg.ToString(), App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show("An error occured. Check the log.", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
        }
    }
    private bool CanRestore() => selectedItems.Any();


    public IRelayCommand AboutCommand { get; }
    private void OnAbout()
    {
        var explorerVersion = Environment.GetEnvironmentVariable("ClickOnce_CurrentVersion") ?? "0.0.0.0"; // https://stackoverflow.com/a/75263211/1582323  //System.Deployment. System.Reflection.Assembly.GetEntryAssembly().GetName().Version; doesnt work

        var coreVersion = typeof(Arius.Core.Facade.Facade).Assembly.GetName().Version;

        MessageBox.Show($"Arius Explorer v{explorerVersion}\nArius Core v{coreVersion}", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }


    public partial class FolderViewModel : ObservableRecipient
    {
        public FolderViewModel()
        {
            Folders = new ObservableCollection<FolderViewModel>();
            Items   = new SortedObservableCollection<ItemViewModel>(new NaturalStringComparer());
        }

        [ObservableProperty]
        private string name;

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        private bool isSelected;

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        private bool isExpanded;

        public string RelativeDirectoryName { get; init; }
        public bool   IsLoaded              { get; set; } = false;

        public ObservableCollection<FolderViewModel>     Folders { get; }
        public SortedObservableCollection<ItemViewModel> Items   { get; } // TODO ReadOnlyCollection public?

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

        public override string ToString() => RelativeDirectoryName;
    }

    public partial class ItemViewModel : ObservableRecipient
    {
        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        private bool isSelected;

        [ObservableProperty]
        private string name;

        
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PointerFileStateColor))]
        private PointerFileInfo? pointerFileInfo;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BinaryFileStateColor))]
        private BinaryFileInfo? binaryFileInfo;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PointerFileEntryStateColor))]
        private string? pointerFileEntryRelativeName;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChunkStateColor))]
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
                    return Brushes.White; // NOT transparent - if the PointerFile is black then the full half circle is black
            }
        }

        public Brush PointerFileEntryStateColor
        {
            get
            {
                if (PointerFileEntryRelativeName is not null)
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
                    Core.Facade.HydrationState.Hydrated         => Brushes.Blue,
                    Core.Facade.HydrationState.NeedsToBeQueried => Brushes.Blue, // for chunked ones - graceful UI for now
                    Core.Facade.HydrationState.Hydrating        => Brushes.DeepSkyBlue,
                    Core.Facade.HydrationState.NotHydrated      => Brushes.LightBlue,
                    null                                        => Brushes.Transparent,
                    _                                           => throw new ArgumentOutOfRangeException()
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

                if (PointerFileEntryRelativeName is not null)
                    s.AppendLine("The remote entry exists");
                else
                    s.AppendLine("The remote entry not exist");

                switch (HydrationState)
                {
                    case Core.Facade.HydrationState.Hydrated:
                        s.AppendLine("The remote file can be restored");
                        break;
                    case Core.Facade.HydrationState.Hydrating:
                        s.AppendLine("The remote file is being hydrated");
                        break;
                    case Core.Facade.HydrationState.NotHydrated:
                        s.AppendLine("The remote file needs to be hydrated first");
                        break;
                    case Core.Facade.HydrationState.NeedsToBeQueried:
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

        [ObservableProperty]
        private long originalLength;

        public override string ToString() => Name;
    }
}