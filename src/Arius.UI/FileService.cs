using System.IO;
using WouterVanRanst.Utils.Extensions;

namespace Arius.UI;

class FileService
{
    public static async IAsyncEnumerable<(string RelativeParentPath, string DirectoryName, string Name)> GetEntriesAsync(
        string? relativeParentPathEquals = null,
        string? directoryNameEquals = null,
        string? nameContains = null)
    {
        var rootDir = new DirectoryInfo("C:\\Users\\woute\\Documents\\AriusTest");
        if (relativeParentPathEquals is not null)
            rootDir = rootDir.GetSubDirectory(relativeParentPathEquals);
        if (directoryNameEquals is not null)
            rootDir.GetSubDirectory(directoryNameEquals);

        foreach (var d in rootDir.EnumerateFiles("*.pointer.arius"))
            yield return (relativeParentPathEquals, GetDirectoryName(rootDir, rootDir), d.Name);

        foreach (var dir2 in rootDir.EnumerateDirectories())
        {
            foreach (var d in dir2.EnumerateFiles("*.pointer.arius"))
                yield return (relativeParentPathEquals, GetDirectoryName(rootDir, dir2), d.Name);
        }

        static string GetDirectoryName(DirectoryInfo root, DirectoryInfo dir)
        {
            if (root == dir)
                return string.Empty;
            else
                return dir.Name;
        }
    }
}