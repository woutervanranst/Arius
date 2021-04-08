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
using Microsoft.Extensions.Hosting;
using System.Threading;
using Microsoft.Extensions.Options;
using Arius.Console;

[assembly: InternalsVisibleTo("Arius.Tests")]
namespace Arius
{
    internal static class Program
    {
        public static async Task Main(string[] args) => await CreateHostBuilder(args).RunConsoleAsync();

        internal static IHostBuilder CreateHostBuilder(string[] args, Action<IConfigurationBuilder> configBuilder = default)
        {
            //Console App with .NET Generic Host based on template from https://dfederm.com/building-a-console-app-with-.net-generic-host/

            var host = Host.CreateDefaultBuilder(args)
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureAppConfiguration(builder =>
                {
                    // https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-5.0#host-configuration

                    builder.AddJsonFile("appsettings.json");
                    builder.AddCommandLine(args); //as per https://github.com/aspnet/MetaPackages/issues/221#issuecomment-335207431

                    if (configBuilder is not null)
                        configBuilder(builder);
                })
                .ConfigureLogging((hostBuilderContext, loggingBuilder) =>
                {
                    loggingBuilder
                        .AddConfiguration(hostBuilderContext.Configuration.GetSection("Logging"))
                        .AddSimpleConsole(options =>
                        {
                            // See for options: https://docs.microsoft.com/en-us/dotnet/core/extensions/console-log-formatter#simple
                        });

                    if (!Environment.GetCommandLineArgs()[0].EndsWith("testhost.dll"))
                    {
                        //We're NOT in a unit test
                        loggingBuilder.AddFile(options =>
                        {
                            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" && Directory.Exists("/logs"))
                                options.RootPath = "/logs";
                            else
                                options.RootPath = AppContext.BaseDirectory;

                            options.Files = new[] { new LogFileOptions { Path = $"arius-{DateTime.Now:yyyyMMdd-HHmmss}.log" } };

                            options.TextBuilder = SingleLineLogEntryTextBuilder.Default;
                        });
                    }
                })
                .ConfigureServices((hostBuilderContext, serviceCollection) =>
                {
                    serviceCollection
                        // Add Commandline Service
                        // h4x0r as per https://stackoverflow.com/a/65552373/1582323 ?
                        //.AddHostedService<AriusHostedService>()
                        .AddSingleton<AriusCommandService>()
                        .AddHostedService<AriusCommandService>(p => p.GetRequiredService<AriusCommandService>())

                        //Add Commmands
                        .AddSingleton<ArchiveCommandExecutor>()
                        .AddSingleton<RestoreCommandExecutor>()


                        // Parsed Command Options
                        .AddSingleton<ICommandExecutorOptions>(p => p.GetRequiredService<AriusCommandService>().CommandExecutorOptions)
                        .AddSingleton<ArchiveOptions>(sp => (ArchiveOptions)sp.GetRequiredService<ICommandExecutorOptions>())
                        .AddSingleton<RestoreOptions>(sp => (RestoreOptions)sp.GetRequiredService<ICommandExecutorOptions>())


                        //Add Services
                        .AddSingleton<PointerService>()
                        .AddSingleton<IHashValueProvider, SHA256Hasher>()
                        .AddSingleton<IEncrypter, SevenZipCommandlineEncrypter>()
                        .AddSingleton<IBlobCopier, AzCopier>()
                        .AddSingleton<AzureRepository>()

                        // Add Chunkers
                        .AddSingleton<Chunker>()
                        .AddSingleton<DedupChunker>()
                        .AddSingleton<IChunker>((sp) =>
                            {
                                var chunkerOptions = (IChunkerOptions)sp.GetRequiredService<ArchiveOptions>();
                                if (chunkerOptions.Dedup)
                                    return sp.GetRequiredService<DedupChunker>();
                                else
                                    return sp.GetRequiredService<Chunker>();
                            });

                    // Add Options
                    serviceCollection.AddOptions<AzCopyAppSettings>().Bind(hostBuilderContext.Configuration.GetSection("AzCopier"));
                    serviceCollection.AddOptions<TempDirectoryAppSettings>().Bind(hostBuilderContext.Configuration.GetSection("TempDir"));

                    // Add ArchiveCommandExecutorBlocks
                    ArchiveCommandExecutor.AddProviders(serviceCollection);
                    RestoreCommandExecutor.AddProviders(serviceCollection);
                })
                ;
            //    // .RunConsoleAsync() -- see here to split into steps: https://stackoverflow.com/questions/53484777/access-iserviceprovider-when-using-generic-ihostbuilder
            //    .UseConsoleLifetime()
            //    .Build();

            //await host.RunAsync();

            //return (Environment.ExitCode, host.Services);

            return host;
        }
    }

    internal class AriusCommandService : IHostedService
    {
        public AriusCommandService(ILogger<AriusCommandService> logger,
            IHostApplicationLifetime appLifetime,
            IServiceProvider serviceProvider)
        {
            this.logger = logger;
            this.appLifetime = appLifetime;
            this.serviceProvider = serviceProvider;

            //TODO error handling met AppDomain.CurrentDomain.FirstChanceException  ?

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                logger.LogError(e.Exception, "UnobservedTaskException", e, sender);
                throw e.Exception;
            };
        }

        private readonly ILogger<AriusCommandService> logger;
        private readonly IHostApplicationLifetime appLifetime;
        private readonly IServiceProvider serviceProvider;
        private int? exitCode;

        public ICommandExecutorOptions CommandExecutorOptions { get; set; }

        internal static readonly string CommandLineEnvironmentVariableName = "ariusCommandLineEnvVarName";

        public Task StartAsync(CancellationToken cancellationToken)
        {
            logger.LogDebug($"Starting");

            appLifetime.ApplicationStarted.Register(() =>
            {
                Task.Run(async () =>
                {
                    try
                    {
                        var parsedCommandProvider = new ParsedCommandProvider();

                        IAriusCommand archiveCommand = new ArchiveCommand();
                        IAriusCommand restoreCommand = new RestoreCommand();

                        var ariusCommand = new RootCommand();
                        ariusCommand.Description = "Arius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier.";
                        ariusCommand.AddCommand(archiveCommand.GetCommand(parsedCommandProvider));
                        ariusCommand.AddCommand(restoreCommand.GetCommand(parsedCommandProvider));

                        string[] args;
                        if (Environment.GetCommandLineArgs()[0].EndsWith("testhost.dll"))
                        {
                            //We're in a unit test
                            args = Environment.GetEnvironmentVariable(CommandLineEnvironmentVariableName)!.Split(' ');
                        }
                        else
                        {
                            //Normal run
                            args = Environment.GetCommandLineArgs();
                        }
                        exitCode = await ariusCommand.InvokeAsync(args);

                        if (exitCode != 0)
                            return; //eg when calling "arius" or "arius archive" without actual parameters -- see the ACTUAL output in the console or in Output.Tests

                        CommandExecutorOptions = parsedCommandProvider.CommandExecutorOptions;

                        var commandExecutor = (ICommandExecutor)serviceProvider.GetRequiredService(parsedCommandProvider.CommandExecutorType);
                        exitCode = await commandExecutor.Execute();
                        
                        logger.LogInformation("Done");
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Unhandled exception");
                        exitCode = 1;
                    }
                    finally
                    {
                        //Delete the tempdir
                        logger.LogInformation("Deleting tempdir...");
                        var tempDir = serviceProvider.GetRequiredService<IOptions<TempDirectoryAppSettings>>().Value.TempDirectory;
                        if (tempDir.Exists)
                            tempDir.Delete(true);
                        logger.LogInformation("Deleting tempdir... done");

                        appLifetime.StopApplication();
                    }
                });
            });

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogDebug($"Exiting with return code: {exitCode}");

            // Exit code may be null if the user cancelled via Ctrl+C/SIGTERM
            Environment.ExitCode = exitCode.GetValueOrDefault(-1);
            return Task.CompletedTask;
        }
    }
}
