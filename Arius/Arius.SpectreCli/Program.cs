using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using Arius.SpectreCli.Commands;
using Arius.SpectreCli.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Core;
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

                config.AddCommand<Commands.ArchiveCliCommand>("archive");
            });

            return app.Run(args);
        }
    }
}