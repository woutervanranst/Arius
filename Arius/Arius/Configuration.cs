using System.IO;
using Arius.CommandLine;
using Microsoft.Extensions.Configuration;

namespace Arius
{
    internal interface IConfigurationOptions : ICommandExecutorOptions
    {
        //public string Path { get; init; }
    }

    internal interface IConfiguration
    {
        DirectoryInfo TempDir { get; }
        long BatchSize { get; }
        int BatchCount { get; }
    }

    internal class Configuration : IConfiguration
    {
        public Configuration(ICommandExecutorOptions options, IConfigurationRoot config)
        {
            //var root = ((ILocalRootDirectoryOptions)options).Path;
            //_root = new DirectoryInfo(root);
            
            ConfigurationRoot = config;

            //Init TempDir
            if (TempDir.Exists) TempDir.Delete(true);
            TempDir.Create();
        }

        //private readonly DirectoryInfo _root;

        public IConfigurationRoot ConfigurationRoot { get; init; }

        public DirectoryInfo TempDir => new DirectoryInfo(Path.Combine(Path.GetTempPath(), ConfigurationRoot["TempDirName"]));

        public long BatchSize => ConfigurationRoot.GetValue<long>("AzCopier:BatchSize");

        public int BatchCount => ConfigurationRoot.GetValue<int>("AzCopier:BatchCount");
    }
}
