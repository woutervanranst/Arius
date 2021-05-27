using Arius.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

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

        internal InvocationContext InvocationContext { get; set; }

        public static async Task<int> Main(string[] args) => await (new Program().Main(args));
        internal async Task<int> Main(string[] args, Core.Facade.IFacade facade = default)
        {
            //Environment.ExitCode = (int)ExitCode.ERROR;

            var r = await GetCommandLineBuilder()
                .UseHost(_ => Host.CreateDefaultBuilder(),
                    host =>
                    {
                        InvocationContext = host.GetInvocationContext();
                        
                        host
                            .ConfigureAppConfiguration(builder =>
                            {
                                builder.AddJsonFile("appsettings.json");
                            })
                            //.ConfigureLogging((hostBuilderContext, loggingBuilder) =>
                            // {

                            // })
                            .ConfigureServices(services =>
                            {
                                if (facade is null)
                                    services.AddSingleton<Core.Facade.IFacade, Arius.Core.Facade.Facade>();
                                else
                                    services.AddSingleton<Core.Facade.IFacade>(facade);

                                //services.Configure<HostOptions>(c => c.)
                                //services.AddSingleton<IGreeter, Greeter>();
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
            
            root.AddCommand(ArchiveCliCommand.GetCommand());
            root.AddCommand(RestoreCliCommand.GetCommand());

            return new CommandLineBuilder(root);
        }
    }
}