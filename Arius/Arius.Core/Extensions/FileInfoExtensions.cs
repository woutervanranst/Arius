using Arius.Core.Models;
using System;
using System.IO;

namespace Arius.Core.Extensions
{
    static class FileInfoExtensions
    {
        public static bool IsPointerFile(this FileInfo fi) => fi.Name.EndsWith(PointerFile.Extension, StringComparison.CurrentCultureIgnoreCase);

        public static FileInfo CopyTo(this FileInfo source, DirectoryInfo targetDir)
        {
            return source.CopyTo(Path.Combine(targetDir.FullName, source.Name));
        }
        public static FileInfo CopyTo(this FileInfo source, DirectoryInfo targetDir, string targetName)
        {
            return source.CopyTo(Path.Combine(targetDir.FullName, targetName));
        }

        public static FileInfo CopyTo(this FileInfo source, string targetName)
        {
            return source.CopyTo(Path.Combine(source.DirectoryName, targetName));
        }

        public static void Rename(this FileInfo source, string targetName)
        {
            source.MoveTo(Path.Combine(source.DirectoryName, targetName));
        }
    }
}
