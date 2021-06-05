using System;
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
using System.Reflection;
using System.Linq;
using System.CommandLine.Builder;

/*
 * This is required to test the internals of the Arius.Cli assembly
 */
[assembly: InternalsVisibleTo("Arius.Cli.Tests")]
namespace Arius
{
    internal sealed class Program
    {
        //Console App with .NET Generic Host based on template from https://dfederm.com/building-a-console-app-with-.net-generic-host/

        private static async Task Main(string[] args)
        {
            await RunConsoleAync(args);
        }




        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        /// <param name="facade">injected facade, if needed (used for Mocking)</param>
        /// <returns></returns>
        internal static async Task RunConsoleAync(string[] args, Core.Facade.IFacade facade = default)
        {
            await Host.CreateDefaultBuilder(args)
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureAppConfiguration(builder =>
                {
                    // https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-5.0#host-configuration

                    builder.AddJsonFile("appsettings.json");
                    builder.AddCommandLine(args); //as per https://github.com/aspnet/MetaPackages/issues/221#issuecomment-335207431
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
                .ConfigureServices((hostContext, services) =>
                {
                    services
                        // Add Commandline Service
                        .AddHostedService<ConsoleHostedService>()

                        //Add Commmands
                        .AddSingleton<ArchiveCliCommand>()
                        .AddSingleton<RestoreCliCommand>();

                    if (facade is null)
                        services.AddSingleton<Core.Facade.IFacade, Arius.Core.Facade.Facade>();
                    else
                        services.AddSingleton<Core.Facade.IFacade>(facade);

                    // Add Options
                    services.AddOptions<Arius.Core.Configuration.AzCopyAppSettings>().Bind(hostContext.Configuration.GetSection("AzCopier"));
                    services.AddOptions<Arius.Core.Configuration.TempDirectoryAppSettings>().Bind(hostContext.Configuration.GetSection("TempDir"));
                })
                .RunConsoleAsync();
        }
    }
}
