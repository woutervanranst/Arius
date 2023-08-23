using System.IO;

namespace Arius.UI;

class FileService
{
    /// <summary>
    /// Returns file entries from a given directory, and from its direct child directories.
    /// </summary>
    public static async IAsyncEnumerable<(string RelativeParentPath, string DirectoryName, string Name)> GetEntriesAsync(DirectoryInfo rootDir, string? relativeParentPathEquals = null)
    {
        // NOTE This method is somewhat enigmatic but it produces consistent results with the GetPointerFileEntriesAtVersionAsync

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



        //// Determine target directory based on provided relativeParentPathEquals
        //DirectoryInfo targetDir;
        //if (string.IsNullOrEmpty(relativeParentPathEquals))
        //{
        //    targetDir = rootDir;
        //}
        //else
        //{
        //    var adjustedRelativePath = relativeParentPathEquals.Replace('/', Path.DirectorySeparatorChar);
        //    targetDir = new DirectoryInfo(Path.Combine(rootDir.FullName, adjustedRelativePath));
        //}

        //// If the target directory doesn't exist, we have nothing more to do.
        //if (!targetDir.Exists) yield break;

        //foreach (var file in Kak(targetDir))
        //{
        //    if (relativeParentPathEquals is not null)
        //    {
        //        if (file.Directory.Parent.Name == relativeParentPathEquals)
        //            yield return (GetRelativePath(rootDir.FullName, targetDir.FullName), file.Directory.Name, file.Name);
        //    }

        //}

        //string GetRelativePath(string relativeTo, string path)
        //{
        //    var p = Path.GetRelativePath(relativeTo, path);
        //    return p == "." ? "" : p;
        //}

        //static IEnumerable<FileInfo> Kak(DirectoryInfo dir)
        //{
        //    foreach (var f in dir.EnumerateFiles())
        //        yield return f;

        //    foreach (var d in dir.EnumerateDirectories())
        //    {
        //        foreach (var f in d.EnumerateFiles())
        //            yield return f;
        //    }
        //    {

        //    }
        //}
    }
}