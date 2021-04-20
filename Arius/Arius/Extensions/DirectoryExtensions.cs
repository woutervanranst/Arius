using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Extensions
{
    static class DirectoryExtensions
    {
        public static FileInfo[] TryGetFiles(this DirectoryInfo d, string searchPattern)
        {
            try
            {
                return d.GetFiles(searchPattern, SearchOption.AllDirectories);
            }
            catch (UnauthorizedAccessException)
            {
                return Array.Empty<FileInfo>();
            }
            catch (DirectoryNotFoundException)
            {
                return Array.Empty<FileInfo>();
            }
        }


        public static void DeleteEmptySubdirectories(this DirectoryInfo parentDirectory, bool includeSelf = false)
        {
            DeleteEmptySubdirectories(parentDirectory.FullName);

            if (includeSelf)
                if (!parentDirectory.EnumerateFileSystemInfos().Any())
                    parentDirectory.Delete();
        }
        private static void DeleteEmptySubdirectories(string parentDirectory)
        {
            Parallel.ForEach(Directory.GetDirectories(parentDirectory), directory =>
            {
                DeleteEmptySubdirectories(directory);
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
            });
        }


        public static void CopyTo(this DirectoryInfo sourceDir, string targetDir)
        {
            CopyTo(sourceDir.FullName, targetDir);
        }
        public static void CopyTo(this DirectoryInfo sourceDir, DirectoryInfo targetDir)
        {
            CopyTo(sourceDir.FullName, targetDir.FullName);
        }
        private static void CopyTo(this string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)));

            foreach (var directory in Directory.GetDirectories(sourceDir))
                CopyTo(directory, Path.Combine(targetDir, Path.GetFileName(directory)));
        }

        
        public static bool IsEmpty(this DirectoryInfo dir)
        {
            return !dir.GetFileSystemInfos().Any();
        }


        public static IEnumerable<FileInfo> GetAllFiles(this DirectoryInfo directoryInfo)
        {
            return directoryInfo.GetFiles("*", SearchOption.AllDirectories);
        }
        public static IEnumerable<FileInfo> GetBinaryFiles(this DirectoryInfo directoryInfo)
        {
            return directoryInfo.GetAllFiles().Where(fi => !fi.IsPointerFile());
        }

        public static IEnumerable<FileInfo> GetPointerFiles(this DirectoryInfo directoryInfo)
        {
            return directoryInfo.GetAllFiles().Where(fi => fi.IsPointerFile());
        }
    }
}
