using System.IO;
using WouterVanRanst.Utils.Extensions;

namespace Arius.UI;

class FileService
{
    //public static async IAsyncEnumerable<(string RelativeParentPath, string DirectoryName, string Name)> GetEntriesAsync(
    //    DirectoryInfo rootDir,
    //    string? relativeParentPathEquals = null,
    //    string? directoryNameEquals = null,
    //    string? nameContains = null)
    //{
    //    if (relativeParentPathEquals is not null)
    //        rootDir = rootDir.GetSubDirectory(relativeParentPathEquals);
    //    if (directoryNameEquals is not null)
    //        rootDir.GetSubDirectory(directoryNameEquals);

    //    foreach (var d in rootDir.EnumerateFiles("*.pointer.arius"))
    //        yield return (relativeParentPathEquals, GetDirectoryName(rootDir, rootDir), d.Name);

    //    foreach (var dir2 in rootDir.EnumerateDirectories())
    //    {
    //        foreach (var d in dir2.EnumerateFiles("*.pointer.arius"))
    //            yield return (relativeParentPathEquals, GetDirectoryName(rootDir, dir2), d.Name);
    //    }

    //    static string GetDirectoryName(DirectoryInfo root, DirectoryInfo dir)
    //    {
    //        if (root == dir)
    //            return string.Empty;
    //        else
    //            return dir.Name;
    //    }
    //}

    public static async IAsyncEnumerable<(string RelativeParentPath, string DirectoryName, string Name)> GetEntriesAsync(
        DirectoryInfo rootDir,
        string? relativeParentPathEquals = null)
    {
        //// If no relativeParentPathEquals is provided, return files from the root directory
        //if (string.IsNullOrEmpty(relativeParentPathEquals))
        //{
        //    foreach (var file in rootDir.GetFiles())
        //    {
        //        yield return ("", "", file.Name);
        //    }
        //    yield break;
        //}

        // Convert relative path to system-specific directory path
        string adjustedRelativePath = relativeParentPathEquals.Replace('/', Path.DirectorySeparatorChar);
        var    targetDir            = new DirectoryInfo(Path.Combine(rootDir.FullName, adjustedRelativePath));

        // Ensure the target directory exists before proceeding
        if (!targetDir.Exists) yield break;

        string directoryRelativeToRoot = targetDir.FullName.Substring(rootDir.FullName.Length).TrimStart(Path.DirectorySeparatorChar);

        string[] pathSegments = directoryRelativeToRoot.Split(Path.DirectorySeparatorChar);

        // Deduce the DirectoryName and RelativeParentPath from the pathSegments
        string directoryName      = pathSegments.Length > 0 ? pathSegments[pathSegments.Length - 1] : "";
        string relativeParentPath = pathSegments.Length > 1 ? string.Join(Path.DirectorySeparatorChar, pathSegments, 0, pathSegments.Length - 1) : "";

        // Return files in the target directory
        foreach (var file in targetDir.GetFiles())
        {
            yield return (relativeParentPath, directoryName, file.Name);
        }
    }
}