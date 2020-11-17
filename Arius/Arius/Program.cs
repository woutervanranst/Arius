using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Configuration;
using System.IO;
using System.Runtime.CompilerServices;
using Arius.CommandLine;
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

            var rootCommand = new RootCommand();
            rootCommand.Description = "Arius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier.";

            rootCommand.AddCommand(archiveCommand.GetCommand(pcp));
            //rootCommand.AddCommand(RestoreCommand.GetCommand());

            var r = rootCommand.InvokeAsync(args).Result;


            var configurationRoot = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();



            configurationRoot.GetSection("Logging:File")["PathFormat"] = "arius-{Date}-" + $"{DateTime.Now:HHMMss}.log";

            var serviceProvider = new ServiceCollection()
                .AddLogging(builder =>
                {
                    builder.AddConfiguration(configurationRoot.GetSection("Logging"))
                        .AddConsole()
                        .AddFile(configurationRoot.GetSection("Logging:File"));

                    //builder.AddFilter(((provider, category, logLevel) =>
                    //    {
                    //        return true;
                    //    }))
                    //    .AddConsole(configure => configure.. LogLevel.Information)
                    //    .AddFile("arius-{Date}-" + $"{DateTime.Now:HHMMss}.log", LogLevel.Trace);
                })

                //.AddSingleton(new LoggerFactory()
                //    .AddConsole())
                //.AddLogging(builder =>
                //{
                //    configurationRoot.GetSection("Logging");
                //})

                //.AddLogging( builder =>
                //{
                //    builder.AddConsole();
                   
                //    //builder.AddFile($"arius-{DateTime.Now:hhmmss}.log");
                //    builder.AddFile("arius-{Date}-" + $"{DateTime.Now:HHMMss}.log", LogLevel.Trace);

                //    builder.AddFilter( (provider, category, logLevel) =>
                //    {
                //        if (provider.Contains("ConsoleLoggerProvider"))
                //            return logLevel >= LogLevel.Warning;

                //        //.AddFilter(ll => ll >= LogLevel.Trace)
                //        return false;
                //    });
                //})
                .AddSingleton<ICommandExecutorOptions>(pcp.CommandExecutorOptions)
                .AddSingleton<LocalRootDirectory>()
                .AddSingleton<LocalFileFactory>()
                .AddSingleton<IHashValueProvider, SHA256Hasher>()
                .AddSingleton<IChunker<LocalContentFile>>(
                    ((IChunkerOptions)pcp.CommandExecutorOptions).Dedup ? 
                        new DedupChunker() : 
                        new Chunker())
                .AddSingleton<SevenZipEncrypter<IChunk<LocalContentFile>>>()
                .AddScoped<ArchiveCommandExecutor>()
                .BuildServiceProvider();


            var commandExecutor = (ICommandExecutor)serviceProvider.GetRequiredService(pcp.CommandExecutorType);

            return commandExecutor.Execute();
        }
    }
}
