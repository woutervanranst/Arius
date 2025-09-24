using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Arius.Explorer.RepositoryExplorer;

public partial class TreeNodeViewModel : ObservableObject
{
    private readonly string prefix;
    private readonly Action<TreeNodeViewModel>? onSelected;

    public string Prefix => prefix;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    private ObservableCollection<TreeNodeViewModel> folders = [];

    [ObservableProperty]
    private ObservableCollection<FileItemViewModel> items = [];

    public TreeNodeViewModel(string prefix, Action<TreeNodeViewModel>? onSelected = null, bool showPlaceholder = true)
    {
        this.prefix = prefix;
        this.onSelected = onSelected;

        // Add placeholder child to show expansion chevron
        if (showPlaceholder)
        {
            folders = [new TreeNodeViewModel("", null, false) { Name = "Loading..." }];
        }
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            IsExpanded = true; // Expand when selected
            onSelected?.Invoke(this);
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            IsSelected = true; // Select when expanded
        }
    }
}