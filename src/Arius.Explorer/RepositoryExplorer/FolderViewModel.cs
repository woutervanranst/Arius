using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Arius.Explorer.RepositoryExplorer;

public partial class FolderViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<FileItemViewModel> items = [];

    public FolderViewModel()
    {
        // Initialize with sample data for development
        Items = [
            new FileItemViewModel("document1.pdf", 1024 * 1024),
            new FileItemViewModel("image1.jpg", 2048 * 1024),
            new FileItemViewModel("video1.mp4", 50 * 1024 * 1024)
        ];
    }
}