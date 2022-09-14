using Arius.Core.Models;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Arius.Core.Extensions;

public static class FileInfoExtensions
{
    internal static bool IsPointerFile(this FileInfo fi) => fi.Name.EndsWith(PointerFile.Extension, StringComparison.CurrentCultureIgnoreCase);


    /// <summary>
    /// Copy to targetDir preserving the original filename
    /// </summary>
    internal static FileInfo CopyTo(this FileInfo source, DirectoryInfo targetDir)
    {
        return source.CopyTo(Path.Combine(targetDir.FullName, source.Name));
    }
    /// <summary>
    /// Copy to targetDir with the given name
    /// </summary>
    internal static FileInfo CopyTo(this FileInfo source, DirectoryInfo targetDir, string targetName)
    {
        return source.CopyTo(Path.Combine(targetDir.FullName, targetName));
    }

    /// <summary>
    /// Copy to the targetDir in the same relative path as vs the sourceRoot
    /// eg. CopyTo('dir1\documents\file.txt', 'dir1', 'dir2') results in 'dir2\documents\file.txt')
    /// </summary>
    internal static FileInfo CopyTo(this FileInfo source, DirectoryInfo sourceRoot, DirectoryInfo targetDir, bool overwrite = false)
    {
        if (!source.IsInDirectoryTree(sourceRoot))
            throw new ArgumentException($"{source.FullName} is not in the source directory {sourceRoot.FullName}");

        var relativeName = Path.GetRelativePath(sourceRoot.FullName, source.FullName);
        var target = new FileInfo(Path.Combine(targetDir.FullName, relativeName));
        target.Directory.Create();

        if (!target.Exists || (target.Exists && overwrite))
        {
            source.CopyTo(target.FullName, overwrite);
            target.LastWriteTimeUtc = source.LastWriteTimeUtc; //CopyTo does not do this somehow
        }

        return target;
    }

    /// <summary>
    /// Checks whether fi is in the directory tree under parent, recursively
    /// eg. c:\test\dir1\file1.txt, c:\test\dir1 is true
    /// eg. c:\test\dir1\file1.txt, c:\test\abcd is false
    /// </summary>
    /// <param name="fi"></param>
    /// <param name="parent"></param>
    /// <returns></returns>
    internal static bool IsInDirectoryTree(this FileInfo fi, DirectoryInfo parent)
    {
        var dir = fi.Directory;
        while (true)
        {
            if (String.Compare(dir.FullName, parent.FullName, StringComparison.OrdinalIgnoreCase) == 0)
                return true;

            dir = dir.Parent;

            if (dir is null)
                return false;
        }
    }

    internal static void Rename(this FileInfo source, string targetName)
    {
        source.MoveTo(Path.Combine(source.DirectoryName, targetName));
    }

    internal static string GetRelativeName(this FileInfo fi, DirectoryInfo root) => Path.GetRelativePath(root.FullName, fi.FullName);

    public static async Task CompressAsync(this FileInfo fi, bool deleteOriginal)
    {
        await using (var ss = fi.OpenRead())
        {
            await using var ts = File.OpenWrite($"{fi.FullName}.gzip");
            await using var gzs = new GZipStream(ts, CompressionLevel.Optimal);
            await ss.CopyToAsync(gzs);
        }

        if (deleteOriginal)
            fi.Delete();
    }
}