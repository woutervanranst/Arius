using System.Runtime.InteropServices;
using Arius.UI.ViewModels;

namespace Arius.UI.Utils;

internal class NaturalStringComparer : IComparer<ExploreRepositoryViewModel.ItemViewModel>
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string x, string y);

    public int Compare(ExploreRepositoryViewModel.ItemViewModel? x, ExploreRepositoryViewModel.ItemViewModel? y)
    {
        return StrCmpLogicalW(x.Name, y.Name);
    }
}