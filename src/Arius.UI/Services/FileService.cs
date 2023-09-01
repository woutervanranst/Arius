using Arius.Core.Queries;
using System.IO;
using WouterVanRanst.Utils.Extensions;

namespace Arius.UI.Services;

internal class FileService
{
    public static async IAsyncEnumerable<string> QuerySubdirectories(DirectoryInfo root, string prefix, int depth)
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
            ? root 
            : new DirectoryInfo(Path.Combine(root.FullName, prefix));

        if (!startingDir.Exists)
            yield break;

        await foreach (var dir in QuerySubdirectoriesRecursive(startingDir, depth))
            yield return dir.FullName[root.FullName.Length..].TrimStart(Path.DirectorySeparatorChar);


        static async IAsyncEnumerable<DirectoryInfo> QuerySubdirectoriesRecursive(DirectoryInfo root, int depth)
        {
            if (depth == 0 || !root.Exists)
                yield break;

            foreach (var subDir in root.GetDirectories())
            {
                yield return subDir;

                await foreach (var nextSubDir in QuerySubdirectoriesRecursive(subDir, depth - 1))
                    yield return nextSubDir;
            }
        }
    }

    
    public record GetLocalEntriesResult(string RelativeName) : IEntryQueryResult;

    public static async IAsyncEnumerable<IEntryQueryResult> QueryFiles(DirectoryInfo root, string relativePath)
    {
        var di = new DirectoryInfo(Path.Combine(root.FullName, relativePath));

        if (!di.Exists)
            yield break;

        foreach (var fi in di.EnumerateFiles())
            yield return new GetLocalEntriesResult(fi.GetRelativeName(root));
    }
}