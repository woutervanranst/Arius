using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;
using Arius.Services;
using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

[assembly: InternalsVisibleTo("Arius.Tests")]
namespace Arius
{
    internal class Program
    {
        private static async Task<int> Main(string[] args)
        {
            var parsedCommandProvider = new ParsedCommandProvider();

            IAriusCommand archiveCommand = new ArchiveCommand();
            IAriusCommand restoreCommand = new RestoreCommand();

            var rootCommand = new RootCommand();
            rootCommand.Description = "Arius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier.";

            rootCommand.AddCommand(archiveCommand.GetCommand(parsedCommandProvider));
            rootCommand.AddCommand(restoreCommand.GetCommand(parsedCommandProvider));

            var r = rootCommand.InvokeAsync(args).Result;

            if (r != 0)
                return r; //eg when calling "arius" or "arius archive" without actual parameters

            var configurationRoot = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var config = new Configuration(parsedCommandProvider.CommandExecutorOptions, configurationRoot);

            var serviceProvider = GetServiceProvider(config, parsedCommandProvider);

            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            //TODO ergens zetten in de Main() ofzo
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                logger.LogError(e.Exception, "UnobservedTaskException", e, sender);
                throw e.Exception;
            };

            //TODO error handling met AppDomain.CurrentDomain.FirstChanceException  ?

            try
            {
                var commandExecutor = (ICommandExecutor) serviceProvider.GetRequiredService(parsedCommandProvider.CommandExecutorType);

                r = await commandExecutor.Execute();

                logger.LogInformation("Done");

                return r;
            }
            catch (Exception e)
            {
                logger.LogCritical(e.ToString());

                return int.MinValue;
            }
            finally
            {
                logger.LogInformation("Deleting tempdir...");

                //Delete the tempdir
                config.UploadTempDir.Delete(true);

                logger.LogInformation("Deleting tempdir... done");
            }
        }

        internal static ServiceProvider GetServiceProvider(Configuration config, ParsedCommandProvider pcp)
        {
            var serviceCollection = new ServiceCollection()
                .AddLogging(builder =>
                {
                    builder.AddConfiguration(config.ConfigurationRoot.GetSection("Logging"))
                        .AddSimpleConsole(options =>
                        {
                            // See for options: https://docs.microsoft.com/en-us/dotnet/core/extensions/console-log-formatter#simple
                        })
                        .AddFile(options =>
                        {
                            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" && Directory.Exists("/logs"))
                                options.RootPath = "/logs";
                            else
                                options.RootPath = AppContext.BaseDirectory;

                            options.Files = new [] { new LogFileOptions { Path = $"arius-{DateTime.Now:yyyyMMdd-HHmmss}.log" } };

                            options.TextBuilder = SingleLineLogEntryTextBuilder.Default; //  Karambolo.Extensions.Logging.File.FileLogEntryTextBuilder.Instance;
                        });
                })
                .AddSingleton<IConfiguration>(config)
                .AddSingleton<ICommandExecutorOptions>(pcp.CommandExecutorOptions)

                //Add Services
                .AddSingleton<PointerService>()
                .AddSingleton<IHashValueProvider, SHA256Hasher>()
                .AddSingleton<IEncrypter, SevenZipCommandlineEncrypter>()
                .AddSingleton<IBlobCopier, AzCopier>()

                //Add Commmands
                .AddSingleton<ArchiveCommandExecutor>()
                .AddSingleton<RestoreCommandExecutor>()

                .AddSingleton<AzureRepository>();

            // Add Chunkers
            if (((IChunkerOptions)pcp.CommandExecutorOptions).Dedup)
                serviceCollection.AddSingleton<IChunker, DedupChunker>();
            else
                serviceCollection.AddSingleton<IChunker, Chunker>();
            serviceCollection.AddSingleton<Chunker>();
            serviceCollection.AddSingleton<DedupChunker>();

            return serviceCollection.BuildServiceProvider();
        }
    }
}
