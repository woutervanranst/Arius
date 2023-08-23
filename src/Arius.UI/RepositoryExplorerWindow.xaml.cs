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
        Folders = new();

        messenger.Register<PropertyChangedMessage<bool>>(this, HandlePropertyChange);

        // Set the selected folder to the root and kick off the loading process
        SelectedFolder = AddRootFolder(); //new FolderViewModel { Name = "Root", RelativePath = "", Parent = null };
    }

    private void HandlePropertyChange(object recipient, PropertyChangedMessage<bool> message)
    {
        // TODO this is not called twice on startup?

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
        var x = await repository
            .GetEntriesAsync(SelectedFolder.RelativePath ?? "")
            //.GetEntriesAsync(SelectedFolder.Parent?.FullPath ?? "")
            .ToListAsync();

        var y = await FileService
            //.GetEntriesAsync(SelectedFolder.Parent?.FullPath ?? "")
            .GetEntriesAsync(SelectedFolder.RelativePath ?? "")
            .ToListAsync();

        await foreach (var e in repository
                           .GetEntriesAsync(SelectedFolder.Parent?.RelativePath ?? ""))
        {
            var relativePath       = Path.Combine("root", e.RelativeParentPath, e.DirectoryName); //parent.RelativePath;
            if (!f.TryGetValue(relativePath, out var folder))
            {
                var relativeParentPath = Path.Combine("root", e.RelativeParentPath);
                //var parentFolder = f.TryGetValue(relativeParentPath, out var p) ? p : null;
                var parentFolder = f[relativeParentPath]; //, out var p) ? p : null;
                f.Add(relativePath, folder = new FolderViewModel { Name = e.DirectoryName, RelativePath = relativePath, Parent = parentFolder });

                //if (parentFolder is null)
                //    Folders.Add(folder); // this is the root node we're adding
                //else
                    parentFolder.Folders.Add(folder);
            }

            folder.Items.Add(new ItemViewModel { Name = e.Name });

        }
    }

    private readonly Dictionary<string, FolderViewModel> f = new();

    private FolderViewModel AddRootFolder()
    {
        var root = new FolderViewModel { Name = "Root", RelativePath = "", Parent = null };
        f.Add("root", root);
        Folders.Add(root);

        return root;
    }


    [ObservableProperty]
    private ObservableCollection<FolderViewModel> folders;

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


    //[ObservableProperty]
    //private ObservableCollection<ItemViewModel> items;

    //[ObservableProperty]
    //private ItemViewModel selectedItem;

    public partial class FolderViewModel : ObservableRecipient
    {
        public FolderViewModel()
        {
            Folders = new ObservableCollection<FolderViewModel>();
            Items   = new ObservableCollection<ItemViewModel>();
        }

        public string Name         { get; init; }
        public string RelativePath { get; init; }

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
        public ObservableCollection<ItemViewModel>   Items          { get; }

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        private bool isSelected;
        //public bool IsSelected
        //{
        //    get => isSelected;
        //    set
        //    {
        //        if (SetProperty(ref isSelected, value))
        //        {
        //            if (value) // Load entries when expanded.
        //            {
        //                //LoadEntriesAsync();
        //            }
        //        }
        //    }
        //}
        //private bool isSelected;

        public bool IsExpanded
        {
            get => isExpanded;
            set
            {
                if (SetProperty(ref isExpanded, value))
                {
                    //if (value) // Load entries when expanded.
                    //    LoadEntriesAsync();
                }
            }
        }
        //[ObservableProperty]
        //[NotifyPropertyChangedRecipients]
        private bool isExpanded;

        public override string ToString() => Name;
    }

    public partial class ItemViewModel : ObservableObject
    {
        public string Name { get; set; }

        public override string ToString() => Name;
    }
}