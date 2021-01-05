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

            if (r != 0)
                return r; //eg when calling "arius" or "arius archive" without actual parameters

            var configurationRoot = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var config = new Configuration(pcp.CommandExecutorOptions, configurationRoot);

            var serviceProvider = GetServiceProvider(config, pcp);

            try
            {
                var commandExecutor = (ICommandExecutor) serviceProvider.GetRequiredService(pcp.CommandExecutorType);

                return commandExecutor.Execute();
            }
            catch (Exception e)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
                logger.LogCritical(e.ToString());

                return int.MinValue;
            }
            finally
            {
                //Delete the tempdir
                config.TempDir.Delete(true);
            }
        }

        internal static ServiceProvider GetServiceProvider(Configuration config, ParsedCommandProvider pcp)
        {
            var serviceProvider = new ServiceCollection()
                .AddLogging(builder =>
                {
                    //Hack to override the 'fileLoggingConfigurationSection["PathFormat"]'
                    var fileLoggingConfigurationSection = config.ConfigurationRoot.GetSection("Logging:File");
                    if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" && Directory.Exists("/logs"))
                        fileLoggingConfigurationSection["PathFormat"] = Path.Combine(@"/logs", "arius-{Date}-" + $"{DateTime.Now:HHmmss}.log");
                    else
                        fileLoggingConfigurationSection["PathFormat"] = "arius-{Date}-" + $"{DateTime.Now:HHmmss}.log";


                    builder.AddConfiguration(config.ConfigurationRoot.GetSection("Logging"))
                        .AddSimpleConsole(options =>
                        {
                            //TODO https://docs.microsoft.com/en-us/dotnet/core/extensions/console-log-formatter#set-formatter-with-configuration
                            // https://docs.microsoft.com/en-us/dotnet/core/extensions/console-log-formatter#simple
                            // Alternative: https://stackoverflow.com/questions/44230373/is-there-a-way-to-format-the-output-format-in-net-core-logging/64967936#64967936
                            options.SingleLine = true;
                            options.IncludeScopes = false;
                            options.TimestampFormat = "HH:mm:ss ";
                        })
                        .AddFile(fileLoggingConfigurationSection);
                })
                .AddSingleton<IConfiguration>(config)
                .AddSingleton<ICommandExecutorOptions>(pcp.CommandExecutorOptions)
                //Add Repositories
                .AddSingleton<AriusRepository>()
                .AddSingleton<LocalRootRepository>()
                .AddSingleton<LocalManifestFileRepository>()
                .AddSingleton<RemoteEncryptedChunkRepository>()
                //Add Services
                .AddSingleton<LocalFileFactory>()
                .AddSingleton<RemoteBlobFactory>()
                .AddSingleton<ManifestService>()
                .AddSingleton<PointerService>()
                .AddSingleton<IHashValueProvider, SHA256Hasher>()
                .AddSingleton<IChunker>(((IChunkerOptions) pcp.CommandExecutorOptions).Dedup ? new DedupChunker() : new Chunker())
                .AddSingleton<IEncrypter, SevenZipCommandlineEncrypter>()
                .AddSingleton<IBlobCopier, AzCopier>()
                //Add Commmands
                .AddSingleton<ArchiveCommandExecutor>()
                .AddSingleton<RestoreCommandExecutor>()
                .BuildServiceProvider();
            return serviceProvider;
        }
    }
}
