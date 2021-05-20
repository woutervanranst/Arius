using System.IO;

namespace Arius.Core.Commands
{
    internal interface ITempDirectoryAppSettings
    {
        string RestoreTempDirectoryName { get; init; }
        DirectoryInfo TempDirectory { get; }
        string TempDirectoryFullName { get; }
        string TempDirectoryName { get; init; }

        DirectoryInfo RestoreTempDirectory(DirectoryInfo root);
    }
}