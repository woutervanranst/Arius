using System;
using System.CommandLine;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Arius.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Threading;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

/*
 * This is required to test the internals of the Arius.Cli assembly
 */
[assembly: InternalsVisibleTo("Arius.Cli.Tests")]
namespace Arius
{
    internal class ConsoleHostedService : IHostedService
    {
        public ConsoleHostedService(ILogger<ConsoleHostedService> logger,
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

        private readonly ILogger<ConsoleHostedService> logger;
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
                        await ExecuteCli();
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Unhandled exception");
                        exitCode = 1;
                    }
                    finally
                    {
                        // Stop the application once the work is done
                        appLifetime.StopApplication();
                    }
                });
            });

            return Task.CompletedTask;
        }

        private async Task ExecuteCli()
        {
            Command archiveCommand = serviceProvider.GetRequiredService<ArchiveCliCommand>().GetCommand();
            Command restoreCommand = serviceProvider.GetRequiredService<RestoreCliCommand>().GetCommand();

            var ariusCommand = new RootCommand();
            ariusCommand.Description = "Arius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier.";

            //foreach (Type t in Assembly.GetExecutingAssembly().GetTypes()
            //    .Where(type => t.GetInterfaces().Contains(typeof(ICliCommand))))
            //{

            //}

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

            //Delete the tempdir
            logger.LogInformation("Deleting tempdir...");
            var tempDir = serviceProvider.GetRequiredService<IOptions<Core.Configuration.TempDirectoryAppSettings>>().Value.TempDirectory;
            if (tempDir.Exists)
                tempDir.Delete(true);
            logger.LogInformation("Deleting tempdir... done");
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
