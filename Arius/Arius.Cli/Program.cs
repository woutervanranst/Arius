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
            //Environment.ExitCode = (int)ExitCode.ERROR;

            var r = await GetCommandLineBuilder()
                .UseHost(_ => Host.CreateDefaultBuilder(),
                    host =>
                    {
                        InvocationContext = host.GetInvocationContext(); //TODO code smell - get this from the IHostBuilder somehow?
                        
                        host
                            .ConfigureAppConfiguration(builder =>
                            {
                                builder.AddJsonFile("appsettings.json");
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
                            .ConfigureServices(services =>
                            {
                                if (facade is null)
                                    services.AddSingleton<IFacade, Facade>();
                                else
                                    services.AddSingleton(facade);

                                //services.Configure<HostOptions>(c => c.)
                            })
                            ;
                    })


                    /* Replace .UseDefaults() by its implementation to allow for custom ExceptionHandler
                     *  UseDefaults() implementation: https://github.com/dotnet/command-line-api/blob/3264927b51a5efda4f612c3c08ea1fc089f4fc35/src/System.CommandLine/Builder/CommandLineBuilderExtensions.cs#L282
                     *  Workaround: https://github.com/dotnet/command-line-api/issues/796#issuecomment-670763630
                     */
                    .UseDefaults()
                     
//                    .UseVersionOption()
//                    .UseHelp()
//                    .UseEnvironmentVariableDirective()
//                    .UseParseDirective()
//                    .UseDebugDirective()
//                    .UseSuggestDirective()
//                    .RegisterWithDotnetSuggest()
//                    .UseTypoCorrections()
//                    .UseParseErrorReporting((int)ExitCode.COMMAND_INCOMPLETE)
//#if DEBUG
//                    .UseExceptionHandler((a, e) =>
//                    {
//                        throw a;
//                        //e.ExitCode = 5;

//                    })
//#else
//                    .UseExceptionHandler()
//#endif
//                    .CancelOnProcessTermination()

                    .Build()
                    .InvokeAsync(args);

            Environment.ExitCode = r;

            return r;
        }

        private static CommandLineBuilder GetCommandLineBuilder()
        {
            var root = new RootCommand();
            root.Description = "Arius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier.";
            
            root.AddCommand(new ArchiveCliCommand().GetCommand());
            root.AddCommand(new RestoreCliCommand().GetCommand());

            return new CommandLineBuilder(root);
        }
    }
}