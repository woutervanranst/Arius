using System.IO;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Tests.Extensions;

public static class ZioExtensions
{
    public static DirectoryInfo ToDirectoryInfo(this DirectoryEntry directoryEntry)
    {
        // This is a simplified conversion. In a real scenario, you might need to
        // map the in-memory path to a temporary physical path if the underlying
        // code truly requires a physical DirectoryInfo.
        // For Zio's MemoryFileSystem, the FullName of a DirectoryEntry is usually
        // sufficient for mocking purposes if the consumer only uses the path string.
        return new DirectoryInfo(directoryEntry.FullName);
    }
}