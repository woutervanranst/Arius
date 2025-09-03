using System.Diagnostics;

namespace Arius.Core.Shared.Storage;

[DebuggerDisplay("CacheDirectory = {cacheDirectory.FullName}")]
internal class StateCache
{
    private readonly DirectoryInfo cacheDirectory;

    public StateCache(DirectoryInfo cacheDirectory)
    {
        this.cacheDirectory = cacheDirectory;
        this.cacheDirectory.Create(); // Ensure the directory exists
    }

    public FileInfo GetStateFilePath(string versionName)
    {
        return new FileInfo(Path.Combine(cacheDirectory.FullName, $"{versionName}.db"));
    }

    public void CopyStateFile(FileInfo sourceFile, FileInfo destinationFile)
    {
        sourceFile.CopyTo(destinationFile.FullName, overwrite: true);
    }
}
