using System.IO;
using Arius.Core.Queries;

namespace Arius.UI.Services;

internal class FileService
{
    public record GetLocalEntriesResult(string RelativeName) : IEntryQueryResult;


    public static async IAsyncEnumerable<string> QuerySubdirectories(DirectoryInfo rootDir, string prefix, int depth)
    {
        /* Spec:
         * write an implementation for this method

            public static async IAsyncEnumerable<string> QuerySubdirectories(DirectoryInfo rootDir, string prefix, int depth)

            given this directory tree structure

            C:\Users\woute\Documents\AriusTest>tree
            Folder PATH listing
            Volume serial number is 5E32-C2BC
            C:.
            ├───Antifragile
            └───SOMEDIR
                ├───dir2
                └───MOREDIR
                    └───directory 1
                        └───fdfsf dfs

            given prefix "" and depth 2

            i expect 

            Antifragile
            SOMEDIR
            SOMEDIR\dir2
            SOMEDIR\MOREDIR


            given prefix "SOMEDIR" and depth 2

            i expect

            SOMEDIR\dir2
            SOMEDIR\MOREDIR\directory 1



            given prefix "SOMEDIR\MOREDIR" and depth 2

            i expect

            SOMEDIR\MOREDIR\directory 1
            SOMEDIR\MOREDIR\directory 1\fdfsf dfs
         */

        // Determine the starting directory based on the given prefix
        var startingDir = string.IsNullOrWhiteSpace(prefix) 
            ? rootDir 
            : new DirectoryInfo(Path.Combine(rootDir.FullName, prefix));

        if (!startingDir.Exists)
            yield break;

        await foreach (var dir in GetDirectoriesRecursive(startingDir, depth))
            yield return dir.FullName[rootDir.FullName.Length..].TrimStart(Path.DirectorySeparatorChar);


        static async IAsyncEnumerable<DirectoryInfo> GetDirectoriesRecursive(DirectoryInfo root, int depth)
        {
            if (depth == 0 || !root.Exists)
                yield break;

            foreach (var subDir in root.GetDirectories())
            {
                yield return subDir;

                await foreach (var nextSubDir in GetDirectoriesRecursive(subDir, depth - 1))
                    yield return nextSubDir;
            }
        }
    }

    







//public static async IAsyncEnumerable<string> QuerySubdirectories(DirectoryInfo rootDir, string prefix, int depth)
//{
//    await foreach (var dir in QuerySubdirectoriesRecursive(rootDir, "", prefix, depth))
//    {
//        yield return dir;
//    }
//}

//private static async IAsyncEnumerable<string> QuerySubdirectoriesRecursive(DirectoryInfo currentDir, string currentPath, string prefix, int depth)
//{
//    // Base case: if depth is zero or directory doesn't exist, return
//    if (depth <= 0 || !currentDir.Exists)
//    {
//        yield break;
//    }

//    var subDirectories = currentDir.GetDirectories();

//    foreach (var dir in subDirectories)
//    {
//        var relativePath = dir.FullName.Substring(currentDir.FullName.Length).TrimStart(Path.DirectorySeparatorChar);
//        var fullPath     = string.IsNullOrEmpty(currentPath) ? relativePath : $"{currentPath}{Path.DirectorySeparatorChar}{relativePath}";

//        if (prefix.StartsWith(fullPath) || string.IsNullOrEmpty(prefix))
//        {
//            yield return fullPath;

//            var newPrefix = prefix.Length > fullPath.Length ? prefix.Substring(fullPath.Length + 1) : string.Empty;

//            await foreach (var subDir in QuerySubdirectoriesRecursive(dir, fullPath, newPrefix, depth - 1))
//            {
//                yield return subDir;
//            }
//        }
//    }
//}



/// <summary>
/// Returns file entries from a given directory, and from its direct child directories.
/// </summary>
public static async IAsyncEnumerable<IEntryQueryResult> GetEntriesAsync(DirectoryInfo rootDir, string? relativeParentPathEquals = null)
    {
        await Task.CompletedTask;

        yield break;

        //// NOTE This method is somewhat enigmatic but it produces consistent results with the GetPointerFileEntriesAtVersionAsync

        //// If no relativeParentPathEquals is provided, return files from the root directory
        //if (string.IsNullOrEmpty(relativeParentPathEquals))
        //    foreach (var file in rootDir.GetFiles())
        //        yield return new GetLocalEntriesResult("", "", file.Name);

        //// Convert relative path to system-specific directory path
        //var adjustedRelativePath = relativeParentPathEquals.Replace('/', Path.DirectorySeparatorChar);
        //var targetDir            = new DirectoryInfo(Path.Combine(rootDir.FullName, adjustedRelativePath));

        //// If the target directory doesn't exist, we have nothing more to do.
        //if (!targetDir.Exists) 
        //    yield break;

        //// Return files from direct child directories
        //foreach (var childDir in targetDir.EnumerateDirectories())
        //    foreach (var childFile in childDir.EnumerateFiles())
        //        yield return new GetLocalEntriesResult(GetRelativePath(rootDir.FullName, targetDir.FullName), childDir.Name, childFile.Name);


        //static string GetRelativePath(string relativeTo, string path)
        //{
        //    var p = Path.GetRelativePath(relativeTo, path);
        //    return p == "." ? "" : p;
        //}



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