using System.IO;
using Arius.CommandLine;
using Microsoft.Extensions.Configuration;

namespace Arius.Console
{
    internal class AzCopyAppSettings
    {
        public long BatchSize { get; init; }
        public int BatchCount { get; init; }
    }

    internal class TempDirectoryAppSettings
    {
        public string TempDirectoryName { get; init; }
        public string TempDirectoryFullName => Path.Combine(Path.GetTempPath(), TempDirectoryName);
        public DirectoryInfo TempDirectory => new DirectoryInfo(TempDirectoryFullName);


        public string RestoreTempDirectoryName { get; init; }
        public DirectoryInfo RestoreTempDirectory(DirectoryInfo root)
        {
            var di = new DirectoryInfo(Path.Combine(root.FullName, RestoreTempDirectoryName));
            if (!di.Exists)
                di.Create();

            return di;
        }
    }
}
