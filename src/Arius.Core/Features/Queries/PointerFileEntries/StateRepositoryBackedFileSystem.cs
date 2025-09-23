using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.StateRepositories;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Features.Queries.PointerFileEntries;

internal class StateRepositoryBackedFileSystem : ReadOnlyFileSystem
{
    private readonly IStateRepository stateRepository;

    public StateRepositoryBackedFileSystem(IStateRepository stateRepository) : base(null)
    {
        this.stateRepository = stateRepository;
    }

    protected override bool DirectoryExistsImpl(UPath path)
    {
        throw new NotSupportedException();
    }

    protected override bool FileExistsImpl(UPath path)
    {
        if (!path.IsAbsolute)
            return false;

        var relativeName = $"{path}{PointerFile.Extension}";
        var entry = stateRepository.GetPointerFileEntry(relativeName, false);
        return entry != null;
    }


    protected override long GetFileLengthImpl(UPath path)
    {
        if (!path.IsAbsolute)
            throw new FileNotFoundException();

        var relativeName = $"{path}{PointerFile.Extension}";
        var entry = stateRepository.GetPointerFileEntry(relativeName, true);

        if (entry?.BinaryProperties == null)
            throw new FileNotFoundException();

        return entry.BinaryProperties.OriginalSize;
    }

    protected override DateTime GetLastWriteTimeImpl(UPath path)
    {
        throw new NotSupportedException();
    }

    protected override DateTime GetCreationTimeImpl(UPath path)
    {
        throw new NotSupportedException();
    }

    protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
    {
        if (searchTarget != SearchTarget.File)
            throw new NotSupportedException("Only file enumeration is supported");

        if (searchPattern != "*")
            throw new NotSupportedException("Only '*' search pattern is supported");

        if (!path.IsAbsolute)
            throw new ArgumentException("Path must be absolute");

        var relativeNamePrefix = path.FullName;
        if (relativeNamePrefix == "/")
            relativeNamePrefix = "/";

        var entries = stateRepository.GetPointerFileEntries(relativeNamePrefix, false);

        foreach (var entry in entries)
        {
            // Convert pointer file name back to binary file path
            var pointerFileName = entry.RelativeName;
            if (pointerFileName.EndsWith(PointerFile.Extension))
            {
                var binaryFileName = pointerFileName.Substring(0, pointerFileName.Length - PointerFile.Extension.Length);
                var binaryPath = new UPath($"/{binaryFileName}");

                // Apply search option filtering
                if (searchOption == SearchOption.TopDirectoryOnly)
                {
                    var relativePath = binaryPath.GetDirectory().FullName;
                    if (relativePath != path.FullName)
                        continue;
                }

                yield return binaryPath;
            }
        }
    }

    protected override void CreateDirectoryImpl(UPath path)
    {
        throw new NotSupportedException();
    }

    protected override void DeleteDirectoryImpl(UPath path, bool isRecursive)
    {
        throw new NotSupportedException();
    }

    protected override void DeleteFileImpl(UPath path)
    {
        throw new NotSupportedException();
    }

    protected override void MoveDirectoryImpl(UPath srcPath, UPath destPath)
    {
        throw new NotSupportedException();
    }

    protected override void MoveFileImpl(UPath srcPath, UPath destPath)
    {
        throw new NotSupportedException();
    }

    protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share)
    {
        throw new NotSupportedException();
    }

    protected override void SetAttributesImpl(UPath path, FileAttributes attributes)
    {
        throw new NotSupportedException();
    }

    protected override FileAttributes GetAttributesImpl(UPath path)
    {
        return FileAttributes.ReadOnly | FileAttributes.Normal;
    }

    protected override void SetCreationTimeImpl(UPath path, DateTime time)
    {
        throw new NotSupportedException();
    }

    protected override void SetLastAccessTimeImpl(UPath path, DateTime time)
    {
        throw new NotSupportedException();
    }

    protected override void SetLastWriteTimeImpl(UPath path, DateTime time)
    {
        throw new NotSupportedException();
    }

    protected override DateTime GetLastAccessTimeImpl(UPath path)
    {
        throw new NotSupportedException();
    }


    protected override IEnumerable<FileSystemItem> EnumerateItemsImpl(UPath path, SearchOption searchOption, SearchPredicate? searchPredicate)
    {
        throw new NotSupportedException();
    }
}