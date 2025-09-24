using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Arius.Explorer.RepositoryExplorer;

public partial class TreeNodeViewModel : ObservableObject
{
    private readonly string prefix;

    [ObservableProperty]
    private string name;
    
    [ObservableProperty]
    private bool isSelected;
    
    [ObservableProperty]
    private bool isExpanded;
    
    [ObservableProperty]
    private ObservableCollection<TreeNodeViewModel> folders = [];

    public TreeNodeViewModel(string prefix)
    {
        this.prefix = prefix;
    }
}