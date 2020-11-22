using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Configuration;
using System.IO;
using System.Runtime.CompilerServices;
using Arius.CommandLine;
using Arius.Models;
using Arius.Repositories;
using Arius.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

[assembly: InternalsVisibleTo("Arius.Tests")]
namespace Arius
{
   

    //public static class ServiceProviderExtensions
    //{
    //    public static R1 Ka<R1, T1, K1>(this IServiceProvider sp, K1 t)
    //    {

    //    }
    //}


    internal class Program
    {
        private static int Main(string[] args)
        {
            var pcp = new ParsedCommandProvider();

            IAriusCommand archiveCommand = new ArchiveCommand();
            IAriusCommand restoreCommand = new RestoreCommand();

            var rootCommand = new RootCommand();
            rootCommand.Description = "Arius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier.";

            rootCommand.AddCommand(archiveCommand.GetCommand(pcp));
            rootCommand.AddCommand(restoreCommand.GetCommand(pcp));

            var r = rootCommand.InvokeAsync(args).Result;


            var configurationRoot = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var config = new Configuration(pcp.CommandExecutorOptions, configurationRoot);

            var serviceProvider = new ServiceCollection()
                .AddLogging(builder =>
                {
                    //Hack
                    var fileLoggingConfigurationSection = configurationRoot.GetSection("Logging:File");
                    fileLoggingConfigurationSection["PathFormat"] = "arius-{Date}-" + $"{DateTime.Now:HHmmss}.log";


                    builder.AddConfiguration(configurationRoot.GetSection("Logging"))
                        .AddConsole()
                        .AddFile(fileLoggingConfigurationSection);
                })
                .AddSingleton<Configuration>(config)
                .AddSingleton<ICommandExecutorOptions>(pcp.CommandExecutorOptions)
                //Add Repositories
                .AddSingleton<AriusRepository>()
                .AddSingleton<LocalRootRepository>()
                .AddSingleton<LocalManifestRepository>()
                .AddSingleton<RemoteEncryptedChunkRepository>()
                //Add Services
                .AddSingleton<LocalFileFactory>()
                .AddSingleton<RemoteBlobFactory>()
                .AddSingleton<IHashValueProvider, SHA256Hasher>()
                .AddSingleton<IChunker>(((IChunkerOptions) pcp.CommandExecutorOptions).Dedup ? new DedupChunker() : new Chunker())
                .AddSingleton<IEncrypter, SevenZipEncrypter>()
                .AddSingleton<IBlobCopier, AzCopier>()
                //Add Commmands
                .AddSingleton<ArchiveCommandExecutor>()
                .AddSingleton<RestoreCommandExecutor>()

                .BuildServiceProvider();

            try
            {
                var commandExecutor = (ICommandExecutor) serviceProvider.GetRequiredService(pcp.CommandExecutorType);

                return commandExecutor.Execute();
            }
            //catch (Exception e)
            //{
            //    throw;
            //}
            finally
            {
                //Delete the tempdir
                config.TempDir.Delete(true);
            }
        }
    }
}
