using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Arius
{
    class Program
    {
        static int Main(string[] args)
        {
            //setup our DI
            var serviceProvider = new ServiceCollection()
                //.AddLogging()
                .AddSingleton<ArchiveCommand>()
                .AddSingleton<SevenZipUtils>()
                .BuildServiceProvider();

            var a = serviceProvider.GetService<ArchiveCommand>();

            var rootCommand = new RootCommand();
            rootCommand.AddCommand(a.GetArchiveCommand());

            rootCommand.Description = "Arius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier.";

            return rootCommand.InvokeAsync(args).Result;
        }
    }

    

    

    
}
