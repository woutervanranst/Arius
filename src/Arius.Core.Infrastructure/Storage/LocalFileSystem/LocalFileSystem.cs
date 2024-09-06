using Arius.Core.Domain.Storage.FileSystem;
using File = Arius.Core.Domain.Storage.FileSystem.File;

namespace Arius.Core.Infrastructure.Storage.LocalFileSystem;

public class LocalFileSystem : IFileSystem
{
    private readonly ILogger<LocalFileSystem> logger;

    public LocalFileSystem(ILogger<LocalFileSystem> logger)
    {
        this.logger = logger;
    }

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase) { "@eaDir", "eaDir", "SynoResource" };
    private static readonly HashSet<string> ExcludedFiles       = new(StringComparer.OrdinalIgnoreCase) { "autorun.ini", "thumbs.db", ".ds_store" };

    /// <summary>
    /// Recursively enumerates files in a specified directory, applying filters to skip hidden, system, or excluded files and directories.
    /// This method performs a depth-first traversal of the directory tree.
    /// </summary>
    /// <param name="directory">The root directory from which to start enumerating files.</param>
    /// <returns>An <see cref="IEnumerable{File}"/> containing the files found in the directory and its subdirectories, filtered based on certain criteria.</returns>
    /// <remarks>
    /// The method first enumerates all files in the current directory that meet the filtering criteria.
    /// After processing the files in the current directory, it then recursively enters each subdirectory,
    /// applying the same logic, thus traversing the directory tree in a depth-first manner.
    /// This means it fully explores the contents of each subdirectory before moving on to the next sibling directory.
    /// </remarks>
    public IEnumerable<File> EnumerateFiles(DirectoryInfo directory)
    {
        if (ShouldSkipDirectory(directory))
        {
            logger.LogWarning("Skipping directory {directory} as it is hidden, system, or excluded", directory.FullName);
            yield break;
        }

        foreach (var fi in directory.EnumerateFiles())
        {
            if (ShouldSkipFile(fi))
            {
                logger.LogWarning("Skipping file {file} as it is hidden, system, or excluded", fi.FullName);
                continue;
            }

            yield return File.FromFullName(fi.FullName);
        }

        foreach (var subDir in directory.EnumerateDirectories())
        {
            foreach (var file in EnumerateFiles(subDir))
            {
                yield return file;
            }
        }

        yield break;


        static bool ShouldSkipDirectory(DirectoryInfo dir)
        {
            return (dir.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 ||
                   ExcludedDirectories.Contains(dir.Name);
        }

        static bool ShouldSkipFile(FileInfo file)
        {
            return (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 ||
                   ExcludedFiles.Contains(Path.GetFileName(file.FullName));
        }
    }
}