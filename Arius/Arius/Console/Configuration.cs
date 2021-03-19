using System.IO;
using Arius.CommandLine;
using Microsoft.Extensions.Configuration;

namespace Arius
{
    internal class AzCopyAppSettings
    {
        public long BatchSize { get; set; }
        public int BatchCount { get; set; }
    }

    internal class TempDirAppSettings
    {
        public string UploadTempDirName { get; set; }
        public string DownloadTempDirName { get; set; }


        public string UploadTempDirFullName => Path.Combine(Path.GetTempPath(), UploadTempDirName);
        public DirectoryInfo UploadTempDir => new DirectoryInfo(UploadTempDirFullName);


        public DirectoryInfo DownloadTempDir(DirectoryInfo root)
        {
            var di = new DirectoryInfo(Path.Combine(root.FullName, DownloadTempDirName));
            if (!di.Exists)
                di.Create();

            return di;
        }
    }
}
