using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.CompilerServices;
using Arius.V4;
using Microsoft.Extensions.DependencyInjection;

[assembly: InternalsVisibleTo("Arius.Tests")]
namespace Arius
{
    internal interface IAriusCommand
    {
        Command GetCommand(ParsedCommandProvider e);
    }

    internal interface ICommandExecutor
    {
        public int Execute();
    }

    internal interface ICommandExecutorOptions
    {

    }

    class ParsedCommandProvider
    {
        public Type CommandExecutorType { get; set; }
        public ICommandExecutorOptions CommandExecutorOptions { get; set; }
    }

    public static class CommandHandlerExtensions
    {
        public static System.CommandLine.Invocation.ICommandHandler Create<T1, T2, T3, T4, T5, T6, T7, T8, T9>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, System.Threading.Tasks.Task<int>> action) => System.CommandLine.Binding.HandlerDescriptor.FromDelegate(action).GetCommandHandler();
    }

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
                .AddLogging()
                .AddSingleton<ICommandExecutorOptions>(pcp.CommandExecutorOptions)
                .AddSingleton<AriusRootDirectory>()
                .AddSingleton<LocalFileFactory>()
                .AddScoped<ArchiveCommandExecutor>()
                //.AddScoped<SevenZipUtils>()
                .BuildServiceProvider();


            var commandExecutor = (ICommandExecutor)serviceProvider.GetRequiredService(pcp.CommandExecutorType);

            return commandExecutor.Execute();

            //var k = new Kak();
            //k.Ha();





        }
    }
}
