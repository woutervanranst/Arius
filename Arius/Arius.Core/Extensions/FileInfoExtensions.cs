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