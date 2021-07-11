using Arius.Core.Models;
using System;
using System.IO;

namespace Arius.Core.Extensions
{
    static class FileInfoExtensions
    {
        public static bool IsPointerFile(this FileInfo fi) => fi.Name.EndsWith(PointerFile.Extension, StringComparison.CurrentCultureIgnoreCase);


        /// <summary>
        /// Copy to targetDir preserving the original filename
        /// </summary>
        public static FileInfo CopyTo(this FileInfo source, DirectoryInfo targetDir)
        {
            return source.CopyTo(Path.Combine(targetDir.FullName, source.Name));
        }
        /// <summary>
        /// Copy to targetDir with the given name
        /// </summary>
        public static FileInfo CopyTo(this FileInfo source, DirectoryInfo targetDir, string targetName)
        {
            return source.CopyTo(Path.Combine(targetDir.FullName, targetName));
        }

        /// <summary>
        /// Copy to the targetDir in the same relative path as vs the sourceRoot
        /// eg. CopyTo('dir1\documents\file.txt', 'dir1', 'dir2') results in 'dir2\documents\file.txt')
        /// </summary>
        public static FileInfo CopyTo(this FileInfo source, DirectoryInfo sourceRoot, DirectoryInfo targetDir, bool overwrite = false)
        {
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
        
        public static void Rename(this FileInfo source, string targetName)
        {
            source.MoveTo(Path.Combine(source.DirectoryName, targetName));
        }

        public static string GetRelativeName(this FileInfo fi, DirectoryInfo root) => Path.GetRelativePath(root.FullName, fi.FullName);
    }
}
