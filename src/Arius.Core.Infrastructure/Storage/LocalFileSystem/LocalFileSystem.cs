using Arius.Core.Domain.Storage.FileSystem;
using File = Arius.Core.Domain.Storage.FileSystem.File;

namespace Arius.Core.Infrastructure.Storage.LocalFileSystem;

internal class LocalFileSystem : IFileSystem
{
    private readonly ILogger<LocalFileSystem> logger;

    public LocalFileSystem(ILogger<LocalFileSystem> logger)
    {
        this.logger = logger;
    }

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase) { "@eaDir", "eaDir", "SynoResource" };
    private static readonly HashSet<string> ExcludedFiles       = new(StringComparer.OrdinalIgnoreCase) { "autorun.ini", "thumbs.db", ".ds_store" };

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

            yield return new File(fi);
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