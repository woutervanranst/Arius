using System;
using System.CommandLine;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Arius.CommandLine;
using Arius.Extensions;
using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Threading;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Linq;

/*
 * This is required to test the internals of the Arius.Cli assembly
 */
[assembly: InternalsVisibleTo("Arius.Cli.Tests")]
namespace Arius
{
    public static class Program
    {
        public static async Task Main(string[] args) => await CreateHostBuilder(args).RunConsoleAsync();

        /// <summary>
        /// Create the HostBuilder
        /// </summary>
        /// <param name="args"></param>
        /// <param name="configBuilder">custom configuration</param>
        /// <param name="facade">injected facade, if needed (used for Mocking)</param>
        /// <returns></returns>
        internal static IHostBuilder CreateHostBuilder(string[] args, 
            Action<IConfigurationBuilder> configBuilder = default,
            Core.Facade.IFacade facade = default)
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
                        //Do not log to file as the Karambola extension disposes itself i a weird way when the IHost is initialized multiple times in one ApplicationDomain during the test suite execution
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

                        .AddSingleton<ArchiveCliCommand>()
                        .AddSingleton<RestoreCliCommand>();

                    if (facade is null)
                        serviceCollection.AddSingleton<Core.Facade.IFacade, Arius.Core.Facade.Facade>();
                    else
                        serviceCollection.AddSingleton<Core.Facade.IFacade>(facade);

                        //.AddSingleton<Arius.Core.Facade.Facade>();

                        ////Add Commmands
                        //.AddSingleton<ArchiveCommandExecutor>()
                        //.AddSingleton<RestoreCommandExecutor>()


                        //// Parsed Command Options
                        //.AddSingleton<ICommandExecutorOptions>(p => p.GetRequiredService<AriusCommandService>().CommandExecutorOptions)
                        //.AddSingleton<ArchiveOptions>(sp => (ArchiveOptions)sp.GetRequiredService<ICommandExecutorOptions>())
                        //.AddSingleton<RestoreOptions>(sp => (RestoreOptions)sp.GetRequiredService<ICommandExecutorOptions>())


                        ////Add Services
                        //.AddSingleton<PointerService>()
                        //.AddSingleton<IHashValueProvider, SHA256Hasher>()
                        //.AddSingleton<IEncrypter, SevenZipCommandlineEncrypter>()
                        //.AddSingleton<IBlobCopier, AzCopier>()
                        //.AddSingleton<AzureRepository>()

                        //// Add Chunkers
                        //.AddSingleton<Chunker>()
                        //.AddSingleton<DedupChunker>()
                        //.AddSingleton<IChunker>((sp) =>
                        //    {
                        //        var chunkerOptions = (IChunkerOptions)sp.GetRequiredService<ArchiveOptions>();
                        //        if (chunkerOptions.Dedup)
                        //            return sp.GetRequiredService<DedupChunker>();
                        //        else
                        //            return sp.GetRequiredService<Chunker>();
                        //    });

                    // Add Options
                    serviceCollection.AddOptions<Arius.Core.Configuration.AzCopyAppSettings>().Bind(hostBuilderContext.Configuration.GetSection("AzCopier"));
                    serviceCollection.AddOptions<Arius.Core.Configuration.TempDirectoryAppSettings>().Bind(hostBuilderContext.Configuration.GetSection("TempDir"));

                    // Add ArchiveCommandExecutorBlocks
                    //ArchiveCommandExecutor.AddProviders(serviceCollection);
                    //RestoreCommandExecutor.AddProviders(serviceCollection);
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
                /*
                 * 
                 * TODO:
                    System.AggregateException
                      HResult=0x80131500
                      Message=An error occurred while writing to logger(s). (Cannot access a disposed object.
                    Object name: 'EventLogInternal'.)
                      Source=Microsoft.Extensions.Logging
                      StackTrace:
                       at Microsoft.Extensions.Logging.Logger.ThrowLoggingError(List`1 exceptions)
                       at Microsoft.Extensions.Logging.Logger.Log[TState](LogLevel logLevel, EventId eventId, TState state, Exception exception, Func`3 formatter)
                       at Microsoft.Extensions.Logging.LoggerExtensions.Log(ILogger logger, LogLevel logLevel, EventId eventId, Exception exception, String message, Object[] args)
                       at Microsoft.Extensions.Logging.LoggerExtensions.Log(ILogger logger, LogLevel logLevel, Exception exception, String message, Object[] args)
                       at Microsoft.Extensions.Logging.LoggerExtensions.LogError(ILogger logger, Exception exception, String message, Object[] args)
                       at Arius.AriusCommandService.<>c__DisplayClass0_0.<.ctor>b__0(Object sender, UnobservedTaskExceptionEventArgs e) in C:\Users\Wouter\Documents\GitHub\Arius\Arius\Arius\Console\Program.cs:line 144
                       at System.Threading.Tasks.TaskScheduler.PublishUnobservedTaskException(Object sender, UnobservedTaskExceptionEventArgs ueea)
                       at System.Threading.Tasks.TaskExceptionHolder.Finalize()

                      This exception was originally thrown at this call stack:
                        [External Code]

                    Inner Exception 1:
                    ObjectDisposedException: Cannot access a disposed object.
                    Object name: 'EventLogInternal'.

                 * 
                 */
                logger.LogError(e.Exception, "UnobservedTaskException", e, sender);

                appLifetime.StopApplication();
                throw e.Exception;
            };
        }

        private readonly ILogger<AriusCommandService> logger;
        private readonly IHostApplicationLifetime appLifetime;
        private readonly IServiceProvider serviceProvider;
        private int? exitCode;

        //public ICommandExecutorOptions CommandExecutorOptions { get; set; }

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
                        //foreach (Type t in Assembly.GetExecutingAssembly().GetTypes()
                        //    .Where(type => t.GetInterfaces().Contains(typeof(ICliCommand))))
                        //{

                        //}
                            
                        Command archiveCommand = serviceProvider.GetRequiredService<ArchiveCliCommand>().GetCommand();
                        Command restoreCommand = serviceProvider.GetRequiredService<RestoreCliCommand>().GetCommand();

                        var ariusCommand = new RootCommand();
                        ariusCommand.Description = "Arius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier.";
                        ariusCommand.AddCommand(archiveCommand);
                        ariusCommand.AddCommand(restoreCommand);

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

                        //CommandExecutorOptions = parsedCommandProvider.CommandExecutorOptions;

                        //var commandExecutor = (ICommandExecutor)serviceProvider.GetRequiredService(parsedCommandProvider.CommandExecutorType);
                        //exitCode = await commandExecutor.Execute();
                        
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
                        var tempDir = serviceProvider.GetRequiredService<IOptions<Core.Configuration.TempDirectoryAppSettings>>().Value.TempDirectory;
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
