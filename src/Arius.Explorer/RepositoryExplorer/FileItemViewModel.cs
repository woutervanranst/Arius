using Arius.Core.Features.Queries.PointerFileEntries;
using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using System.Windows.Media;
using WouterVanRanst.Utils.Extensions;

namespace Arius.Explorer.RepositoryExplorer;

public partial class FileItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string name;
    
    [ObservableProperty]
    private Brush pointerFileStateColor;
    [ObservableProperty]
    private Brush binaryFileStateColor;
    [ObservableProperty]
    private Brush pointerFileEntryStateColor;
    [ObservableProperty]
    private Brush chunkStateColor;

    [ObservableProperty]
    private long originalLength;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private string stateTooltip = "File state unknown";

    public FileItemViewModel(PointerFileEntriesQueryFileResult file)
    {
        var n = file.BinaryFileName ?? file.PointerFileEntry?.RemoveSuffix(".pointer.arius") ?? "UNKNOWN"; // TODO how to properly remove the suffix?
        Name = Path.GetFileName(n);

        PointerFileStateColor      = file.PointerFileName is not null ? Brushes.Black : Brushes.Transparent;
        BinaryFileStateColor       = file.BinaryFileName is not null ? Brushes.Blue : Brushes.White; // NOT transparent - if the PointerFile is black then the full half circle is black
        PointerFileEntryStateColor = file.PointerFileEntry is not null ? Brushes.Black : Brushes.Transparent;
        ChunkStateColor = file.Hydrated switch
        {
            true  => Brushes.Blue,
            false => Brushes.LightBlue,
            null  => Brushes.Transparent,
        };

        OriginalLength = file.OriginalSize;

        //StateTooltip = "File is archived and available";


        // TODO add support for HydrationState.Hydrating
        //        return HydrationState switch
        //        {
        //            Core.Facade.HydrationState.Hydrated         => Brushes.Blue,
        //            Core.Facade.HydrationState.NeedsToBeQueried => Brushes.Blue, // for chunked ones - graceful UI for now
        //            Core.Facade.HydrationState.Hydrating        => Brushes.DeepSkyBlue,
        //            Core.Facade.HydrationState.NotHydrated      => Brushes.LightBlue,
        //            null                                        => Brushes.Transparent,
        //            _                                           => throw new ArgumentOutOfRangeException()
    }
}