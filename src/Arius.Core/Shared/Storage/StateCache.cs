using Arius.Core.Shared.FileSystem;
using System.Diagnostics;
using Zio;
using FileSystemExtensions = Arius.Core.Shared.FileSystem.FileSystemExtensions;

namespace Arius.Core.Shared.Storage;

[DebuggerDisplay("CacheDirectory = {cacheDirectory.FullName}")]
internal class StateCache
{
    private readonly DirectoryEntry cacheDirectory;

    public StateCache(string accountName, string containerName)
    {
        var root = FileSystemExtensions.CreateTempSubdirectory("statecache", true);
        cacheDirectory = root.CreateSubdirectory((UPath)accountName / containerName);
    }

    public FileEntry GetStateFileEntry(string versionName)
    {
        return cacheDirectory.GetFileEntry($"{versionName}.db");
    }

    public IEnumerable<FileEntry> GetStateFileEntries()
    {
        return cacheDirectory.GetFileEntries("*.db");
    }

    public void CopyStateFile(FileEntry sourceFile, FileEntry destinationFile)
    {
        sourceFile.CopyTo(destinationFile.FullName, overwrite: true);
    }
}
