using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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
    internal class Program
    {
        private static int Main(string[] args)
        {
            //var serviceProvider = new ServiceCollection()
            //    //.AddLogging()
            //    .AddScoped<ArchiveCommand>()
            //    .AddScoped<SevenZipUtils>()
            //    .BuildServiceProvider();

            var rootCommand = new RootCommand();
            rootCommand.AddCommand(ArchiveCommand.GetCommand());
            rootCommand.AddCommand(RestoreCommand.GetCommand());

            rootCommand.Description = "Arius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier.";

            return rootCommand.InvokeAsync(args).Result;
        }
    }
}
