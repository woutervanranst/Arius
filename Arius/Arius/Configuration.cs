using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Arius.CommandLine;
using Arius.Repositories;
using Microsoft.Extensions.Configuration;

namespace Arius
{
    internal interface IConfigurationOptions : ICommandExecutorOptions
    {
        //public string Path { get; init; }
    }
    internal class Configuration
    {
        public Configuration(ICommandExecutorOptions options, IConfigurationRoot config)
        {
            //var root = ((ILocalRootDirectoryOptions)options).Path;
            //_root = new DirectoryInfo(root);
            
            _config = config;

            //Init TempDir
            if (TempDir.Exists) TempDir.Delete(true);
            TempDir.Create();
        }

        //private readonly DirectoryInfo _root;

        private readonly IConfigurationRoot _config;

        public DirectoryInfo TempDir => new DirectoryInfo(Path.Combine(Path.GetTempPath(), _config["TempDirName"]));
    }
}
