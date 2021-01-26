using System.IO;
using Arius.CommandLine;
using Microsoft.Extensions.Configuration;

namespace Arius
{
    internal interface IConfigurationOptions : ICommandExecutorOptions
    {
        //public string Path { get; init; }
    }

    internal interface IConfiguration //TODO can be removed i think
    {
        DirectoryInfo UploadTempDir { get; }
        long BatchSize { get; }
        int BatchCount { get; }
        DirectoryInfo DownloadTempDir(DirectoryInfo root);
    }

    internal class Configuration : IConfiguration
    {
        public Configuration(ICommandExecutorOptions options, IConfigurationRoot config)
        {
            //var root = ((ILocalRootDirectoryOptions)options).Path;
            //_root = new DirectoryInfo(root);
            
            ConfigurationRoot = config;

            //Init TempDir
            if (UploadTempDir.Exists) UploadTempDir.Delete(true);
            UploadTempDir.Create();
        }

        //private readonly DirectoryInfo _root;

        public IConfigurationRoot ConfigurationRoot { get; init; }

        public DirectoryInfo UploadTempDir => new DirectoryInfo(Path.Combine(Path.GetTempPath(), ConfigurationRoot["UploadTempDirName"]));
        public DirectoryInfo DownloadTempDir(DirectoryInfo root)
        {
            var di = new DirectoryInfo(Path.Combine(root.FullName, ConfigurationRoot.GetValue<string>("DownloadTempDir")));
            if (!di.Exists)
                di.Create();

            return di;
        }

        public long BatchSize => ConfigurationRoot.GetValue<long>("AzCopier:BatchSize");

        public int BatchCount => ConfigurationRoot.GetValue<int>("AzCopier:BatchCount");
    }
}
