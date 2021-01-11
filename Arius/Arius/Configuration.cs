using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    }
}
