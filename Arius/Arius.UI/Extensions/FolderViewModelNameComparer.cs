using Arius.UI.ViewModels;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Arius.UI.Extensions
{
    internal class FolderViewModelNameComparer : IComparer<FolderViewModel>
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int StrCmpLogicalW(string x, string y);

        public int Compare(FolderViewModel x, FolderViewModel y)
        {
            //return x.Name.CompareTo(y.Name);

            // Compare like how Windows Explorer compares - ie Season 1, Season 2, ... Season 10 (not Season 1, Season 10, ...)
            return StrCmpLogicalW(x.Name, y.Name);
        }
    }
}
