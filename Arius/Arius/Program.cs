using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.CompilerServices;
using Arius.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

            var serviceProvider = new ServiceCollection()
                .AddLogging(builder =>
                {
                    builder.AddConsole().AddFilter(ll => ll >= LogLevel.Warning);
                    //builder.AddFile($"arius-{DateTime.Now:hhmmss}.log");
                    builder.AddFile("arius-{Date}-" + $"{DateTime.Now:hhMMss}.log");
                })
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
                //.AddScoped<SevenZipUtils>()
                .BuildServiceProvider();


            var commandExecutor = (ICommandExecutor)serviceProvider.GetRequiredService(pcp.CommandExecutorType);

            return commandExecutor.Execute();
        }
    }
}
