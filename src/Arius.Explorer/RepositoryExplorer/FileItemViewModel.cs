using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace Arius.Explorer.RepositoryExplorer;

public partial class FileItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string name;
    
    [ObservableProperty]
    private bool isSelected;
    
    [ObservableProperty]
    private long originalLength;
    
    [ObservableProperty]
    private Brush pointerFileStateColor = Brushes.Gray;
    
    [ObservableProperty]
    private Brush binaryFileStateColor = Brushes.LightGray;
    
    [ObservableProperty]
    private Brush pointerFileEntryStateColor = Brushes.Gray;
    
    [ObservableProperty]
    private Brush chunkStateColor = Brushes.LightGray;
    
    [ObservableProperty]
    private string stateTooltip = "File state unknown";

    public FileItemViewModel(string name, long originalLength)
    {
        this.name = name;
        this.originalLength = originalLength;
        
        // Set default colors for demonstration
        PointerFileStateColor = Brushes.Green;
        BinaryFileStateColor = Brushes.LightGreen;
        PointerFileEntryStateColor = Brushes.Blue;
        ChunkStateColor = Brushes.LightBlue;
        StateTooltip = "File is archived and available";
    }
}