using Zio;
using Zio.FileSystems;

namespace Arius.Core.Services;

public class StateCache
{
    private readonly Zio.IFileSystem fileSystem;
    private readonly UPath           cacheRoot;

    public StateCache(Zio.IFileSystem fileSystem, UPath cacheRoot)
    {
        this.fileSystem = fileSystem;
        this.cacheRoot = cacheRoot;
        fileSystem.CreateDirectory(cacheRoot);
    }

    public FileEntry GetStateFilePath(string versionName)
    {
        return fileSystem.GetFileEntry(cacheRoot / $"{versionName}.db");
    }

    public void CopyStateFile(FileEntry sourceFile, FileEntry destinationFile)
    {
        using var sourceStream = sourceFile.Open(FileMode.Open, FileAccess.Read);
        using var destinationStream = destinationFile.Open(FileMode.Create, FileAccess.Write);
        sourceStream.CopyTo(destinationStream);
    }
}
