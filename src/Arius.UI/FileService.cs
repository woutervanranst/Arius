using System.IO;

namespace Arius.UI;

class FileService
{
    /// <summary>
    /// Returns file entries from a given directory, and from its direct child directories.
    /// </summary>
    public static async IAsyncEnumerable<(string RelativeParentPath, string DirectoryName, string Name)> GetEntriesAsync(
        DirectoryInfo rootDir,
        string? relativeParentPathEquals = null)
    {
        // If no relativeParentPathEquals is provided, return files from the root directory
        if (string.IsNullOrEmpty(relativeParentPathEquals))
            foreach (var file in rootDir.GetFiles())
                yield return ("", "", file.Name);

        // Convert relative path to system-specific directory path
        var adjustedRelativePath = relativeParentPathEquals.Replace('/', Path.DirectorySeparatorChar);
        var targetDir            = new DirectoryInfo(Path.Combine(rootDir.FullName, adjustedRelativePath));

        // Return files from direct child directories
        foreach (var childDir in targetDir.EnumerateDirectories())
            foreach (var childFile in childDir.EnumerateFiles())
                yield return (GetRelativePath(rootDir.FullName, targetDir.FullName), childDir.Name, childFile.Name);


        static string GetRelativePath(string relativeTo, string path)
        {
            var p = Path.GetRelativePath(relativeTo, path);
            return p == "." ? "" : p;
        }
    }
}