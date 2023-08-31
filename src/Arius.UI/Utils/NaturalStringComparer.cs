using System.Runtime.InteropServices;
using Arius.UI.ViewModels;

namespace Arius.UI.Utils;

internal class NaturalStringComparer : IComparer<RepositoryExplorerViewModel.ItemViewModel>
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string x, string y);

    public int Compare(RepositoryExplorerViewModel.ItemViewModel? x, RepositoryExplorerViewModel.ItemViewModel? y)
    {
        return StrCmpLogicalW(x.Name, y.Name);
    }
}