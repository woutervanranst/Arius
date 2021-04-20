using System;
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

        public static void DirectoryCopy(this DirectoryInfo sourceDirectory, string destDirName, bool copySubDirs)
        {
            if (!sourceDirectory.Exists)
                throw new DirectoryNotFoundException($"Source directory does not exist or could not be found: {sourceDirectory.FullName}");

            var dirs = sourceDirectory.GetDirectories();

            // If the destination directory doesn't exist, create it.       
            Directory.CreateDirectory(destDirName);

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = sourceDirectory.GetFiles();
            foreach (FileInfo file in files)
            {
                string tempPath = Path.Combine(destDirName, file.Name);
                file.CopyTo(tempPath, false);
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string tempPath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir, tempPath, copySubDirs);
                }
            }
        }
    }

}
