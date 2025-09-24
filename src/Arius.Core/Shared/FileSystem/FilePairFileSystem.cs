using Microsoft.Extensions.Logging;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Shared.FileSystem;

internal class FilePairFileSystem : ComposeFileSystem
{
    private readonly ILogger<FilePairFileSystem> logger;

    public FilePairFileSystem(IFileSystem fileSystem, ILogger<FilePairFileSystem> logger, bool owned = true) : base(fileSystem, owned)
    {
        this.logger = logger;
    }

    protected override IEnumerable<FileSystemItem> EnumerateItemsImpl(UPath path, SearchOption searchOption, SearchPredicate? searchPredicate)
    {
        throw new NotImplementedException();
    }

    protected override UPath ConvertPathToDelegate(UPath path) => path;
    protected override UPath ConvertPathFromDelegate(UPath path) => path;

    protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
    {
        if (searchPattern != "*")
            throw new NotSupportedException();

        var fse = FallbackSafe.GetFileSystemEntry(path);
        if (fse is not DirectoryEntry d)
            throw new NotSupportedException();

        switch (searchTarget)
        {
            case SearchTarget.File:
            {
                foreach (var fe in EnumerateFiles(d, searchOption))
                {
                    var p = fe.Path;
                    if (p.IsPointerFilePath())
                    {
                        // this is a PointerFile
                        var bfp = p.GetBinaryFilePath();
                        if (FallbackSafe.FileExists(bfp))
                        {
                            // 1. BinaryFile exists too - yield nothing here, the BinaryFile will be yielded
                            continue;
                        }
                        else
                        {
                            // 2. BinaryFile does not exist
                            yield return bfp; // yield the path of the (nonexisting) binaryfile
                        }
                    }
                    else
                    {
                        // this is a BinaryFile
                        yield return p;
                    }
                }

                break;
            }
            case SearchTarget.Directory:
            {
                foreach (var dir in EnumerateDirectories(d, searchOption))
                {
                    yield return dir.Path;
                }

                break;
            }
            case SearchTarget.Both:
            default:
                throw new NotSupportedException();
        }
    }

    private static IEnumerable<DirectoryEntry> EnumerateDirectories(DirectoryEntry directory, SearchOption searchOption)
    {
        if (ShouldSkipDirectory(directory))
            yield break;

        foreach (var subDir in directory.EnumerateDirectories())
        {
            if (ShouldSkipDirectory(subDir))
                continue;

            yield return subDir;

            if (searchOption == SearchOption.AllDirectories)
            {
                foreach (var deeper in EnumerateDirectories(subDir, searchOption))
                {
                    yield return deeper;
                }
            }
        }
    }

    private IEnumerable<FileEntry> EnumerateFiles(DirectoryEntry directory, SearchOption searchOption)
    {
        if (ShouldSkipDirectory(directory))
        {
            logger.LogWarning("Skipping directory {directory} as it is hidden, system, or excluded", directory.FullName);
            yield break;
        }

        foreach (var fe in directory.EnumerateFiles())
        {
            if (ShouldSkipFile(fe))
            {
                logger.LogWarning("Skipping file {file} as it is hidden, system, or excluded", fe.FullName);
                continue;
            }

            yield return fe;
        }

        // Only recurse into subdirectories if AllDirectories is specified
        if (searchOption == SearchOption.AllDirectories)
        {
            foreach (var subDir in directory.EnumerateDirectories())
            {
                foreach (var file in EnumerateFiles(subDir, searchOption))
                {
                    yield return file;
                }
            }
        }
    }

    static bool ShouldSkipDirectory(DirectoryEntry dir) =>
        (dir.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 ||
        ExcludedDirectories.Contains(dir.Name);

    static bool ShouldSkipFile(FileEntry file) =>
        (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 ||
        ExcludedFiles.Contains(Path.GetFileName(file.FullName));

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase) { "@eaDir", "eaDir", "SynoResource" };
    private static readonly HashSet<string> ExcludedFiles = new(StringComparer.OrdinalIgnoreCase) { "autorun.ini", "thumbs.db", ".ds_store" };
}