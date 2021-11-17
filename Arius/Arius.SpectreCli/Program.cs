using Arius.SpectreCli.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Examples;

namespace Arius.SpectreCli
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var registrations = new ServiceCollection();
            registrations.AddLogging();

            var registrar = new TypeRegistrar(registrations);

            var app = new CommandApp(registrar);
            app.Configure(config =>
            {
                config.SetInterceptor(new LogInterceptor()); // add the interceptor

                config.SetApplicationName("arius");

                //config.SetExceptionHandler(ex =>
                //{
                //    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                //    return -1;
                //});

                config.AddCommand<Commands.ArchiveCliCommand>("archive");
            });
            
            return app.Run(args);
        }
    }
}