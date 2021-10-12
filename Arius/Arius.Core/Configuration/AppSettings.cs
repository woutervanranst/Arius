using Arius.Core.Commands;
using System.IO;

namespace Arius.Core.Configuration
{
    public class TempDirectoryAppSettings
    {
        public string TempDirectoryName { get; init; }
        public string TempDirectoryFullName => Path.Combine(Path.GetTempPath(), TempDirectoryName);
        public DirectoryInfo TempDirectory => new (TempDirectoryFullName);


        public string RestoreTempDirectoryName { get; init; }

        /// <summary>
        /// Create a temporary folder in the given root where restored files will be stored
        /// </summary>
        /// <returns></returns>
        public DirectoryInfo GetRestoreTempDirectory(DirectoryInfo root)
        {
            var di = new DirectoryInfo(Path.Combine(root.FullName, RestoreTempDirectoryName));
            if (!di.Exists) di.Create();

            return di;
        }
    }
}
