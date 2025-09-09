using Zio;
using Zio.FileSystems;

namespace Arius.Core.Shared.FileSystem;

internal class FilePairFileSystem : ComposeFileSystem
{
    public FilePairFileSystem(IFileSystem fileSystem, bool owned = true) : base(fileSystem, owned)
    {
        IsInMemory = fileSystem.GetLastFallback() is MemoryFileSystem;
    }

    public bool IsInMemory { get; init; }

    public DirectoryEntry CreateTempSubdirectory()
    {
        // For simplicity sake, always use the temp path from the physical filesystem. If we use the MemoryFileSystem, we would get an additional /temp path in our restore folder
        var fullName = Directory.CreateTempSubdirectory("arius-").FullName;

        var pfs = new PhysicalFileSystem();
        return pfs.GetDirectoryEntry(pfs.ConvertPathFromInternal(fullName));
    }

    protected override IEnumerable<FileSystemItem> EnumerateItemsImpl(UPath path, SearchOption searchOption, SearchPredicate? searchPredicate)
    {
        throw new NotImplementedException();
    }

    protected override UPath ConvertPathToDelegate(UPath path) => path;
    protected override UPath ConvertPathFromDelegate(UPath path) => path;

    protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
    {
        // Iterate over all the files in the filesystem, and yield only the binaryfiles or binaryfile-equivalents

        if (searchPattern != "*")
            throw new NotSupportedException();
        if (searchOption != SearchOption.AllDirectories)
            throw new NotSupportedException();
        if (searchTarget != SearchTarget.File)
            throw new NotSupportedException();

        var fsi = FallbackSafe.GetFileSystemEntry(path);
        if (fsi is not DirectoryEntry d)
            throw new NotSupportedException();

        foreach (var fe in EnumerateFiles(d))
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
    }

    private static IEnumerable<FileEntry> EnumerateFiles(DirectoryEntry directory)
    {
        if (ShouldSkipDirectory(directory))
        {
            //logger.LogWarning("Skipping directory {directory} as it is hidden, system, or excluded", directory.FullName);
            yield break;
        }

        foreach (var fe in directory.EnumerateFiles())
        {
            if (ShouldSkipFile(fe))
            {
                //logger.LogWarning("Skipping file {file} as it is hidden, system, or excluded", fi.FullName);
                continue;
            }

            yield return fe;
        }

        foreach (var subDir in directory.EnumerateDirectories())
        {
            foreach (var file in EnumerateFiles(subDir))
            {
                yield return file;
            }
        }

        yield break;


        static bool ShouldSkipDirectory(DirectoryEntry dir) =>
            (dir.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 ||
            ExcludedDirectories.Contains(dir.Name);

        static bool ShouldSkipFile(FileEntry file) =>
            (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 ||
            ExcludedFiles.Contains(Path.GetFileName(file.FullName));
    }

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase) { "@eaDir", "eaDir", "SynoResource" };
    private static readonly HashSet<string> ExcludedFiles = new(StringComparer.OrdinalIgnoreCase) { "autorun.ini", "thumbs.db", ".ds_store" };
}