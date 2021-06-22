using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Invocation;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Karambolo.Extensions.Logging.File;
using Arius.Cli.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Arius.Core.Facade;
using System.IO;
using System.CommandLine.Parsing;
using Arius.Cli.CommandLine;

/*
 * This is required to test the internals of the Arius.Cli assembly
 */
[assembly: InternalsVisibleTo("Arius.Cli.Tests")]
namespace Arius.Cli
{
    public class Program
    {
        public enum ExitCode
        {
            SUCCESS = 0,
            ERROR = 1,
        }

        internal InvocationContext InvocationContext { get; private set; }

        public static async Task<int> Main(string[] args) => await (new Program().Main(args));
        internal async Task<int> Main(string[] args, IFacade facade = default)
        {
            //TaskScheduler.UnobservedTaskException += (sender, e) =>
            //{
            //};

            //AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            //{
            //};

            Console.WriteLine("Arius started.");

            int? r = default;
            DirectoryInfo? tempDir = default;

            try
            {
                var cliParser = GetCommandLineBuilder()
                    .UseHost(_ => Host.CreateDefaultBuilder(), host =>
                    {
                        InvocationContext = host.GetInvocationContext(); //TODO code smell - get this from the IHostBuilder somehow? see https://github.com/dotnet/command-line-api/issues/1025 ? https://github.com/dotnet/command-line-api/issues/1312 ? By design https://github.com/dotnet/command-line-api/blob/3264927b51a5efda4f612c3c08ea1fc089f4fc35/src/System.CommandLine.Hosting.Tests/HostingTests.cs#L357 ?

                        host
                                .ConfigureAppConfiguration(builder =>
                                {
                                    builder.AddJsonFile("appsettings.json");
                                })
                                .ConfigureLogging((hostBuilderContext, loggingBuilder) =>
                                {
                                    loggingBuilder
                                    .AddConfiguration(hostBuilderContext.Configuration.GetSection("Logging"))
                                    //.AddSimpleConsole(options =>
                                    //{
                                    //    // See for options: https://docs.microsoft.com/en-us/dotnet/core/extensions/console-log-formatter#simple
                                    //});
                                    .AddCustomFormatter(options => { });

                                    // Check whether we are running in a unit test
                                    if (!Environment.GetCommandLineArgs()[0].EndsWith("testhost.dll"))
                                    {
                                        /* Do not configure Karambola file logging in a unit test
                                            The Karambola extension disposes itself in a weird way when the IHost is initialized multiple times in one ApplicationDomain during the test suite execution */
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
                                .ConfigureServices((hostContext, services) =>
                                {
                                    if (facade is null)
                                        services.AddSingleton<IFacade, Facade>();
                                    else
                                        services.AddSingleton(facade);

                                    services.AddOptions<Arius.Core.Configuration.AzCopyAppSettings>().Bind(hostContext.Configuration.GetSection("AzCopier"));
                                    services.AddOptions<Arius.Core.Configuration.TempDirectoryAppSettings>().Bind(hostContext.Configuration.GetSection("TempDir"));
                                });
                    })

                    /* Replace .UseDefaults() by its implementation to allow for custom ExceptionHandler
                        *  UseDefaults() implementation: https://github.com/dotnet/command-line-api/blob/3264927b51a5efda4f612c3c08ea1fc089f4fc35/src/System.CommandLine/Builder/CommandLineBuilderExtensions.cs#L282
                        *  Workaround: https://github.com/dotnet/command-line-api/issues/796#issuecomment-670763630
                        */
                    .UseDefaults()
                    .UseExceptionHandler((e, context) =>
                    {
                        // NOTE: Logging is not available here -- https://github.com/dotnet/command-line-api/issues/1311
                        //var logger = context.GetHost().Services.GetRequiredService<ILogger>();
                        HandleUnloggableException(e);
                    })
                    .Build();

                r = await cliParser.InvokeAsync(args);
            }
            catch (Exception e)
            {
                HandleUnloggableException(e);
            }

            Environment.ExitCode = r ?? (int)ExitCode.ERROR;
            return Environment.ExitCode;
        }

        private static void HandleUnloggableException(Exception e)
        {
            Console.WriteLine($"An unhandled exception has occurred before the logging infrastructure was set up:\n{e}");
        }

        private CommandLineBuilder GetCommandLineBuilder()
        {
            var root = new RootCommand
            {
                new ArchiveCliCommand().GetCommand(),
                new RestoreCliCommand().GetCommand()
            };
            root.Description = "Arius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier.";

            return new CommandLineBuilder(root);
        }
    }
}