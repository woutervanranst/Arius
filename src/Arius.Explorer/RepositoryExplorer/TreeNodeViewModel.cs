using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Arius.Explorer.RepositoryExplorer;

public partial class TreeNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private string name;
    
    [ObservableProperty]
    private bool isSelected;
    
    [ObservableProperty]
    private bool isExpanded;
    
    [ObservableProperty]
    private ObservableCollection<TreeNodeViewModel> folders = [];

    public TreeNodeViewModel(string name)
    {
        this.name = name;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        // TODO: Handle selection change, update SelectedFolder in parent ViewModel
    }
}