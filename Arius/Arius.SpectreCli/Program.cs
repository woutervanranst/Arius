using System;
using System.IO;
using Arius.CliSpectre.Commands;
using Arius.CliSpectre.Utils;
using Arius.Core.Commands;
using Arius.Core.Commands.Archive;
using Arius.SpectreCli.Infrastructure;
using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Examples;

namespace Arius.CliSpectre
{
    public static class Program
    {
        public static int Main(string[] args)
        {
//            var logo = @"
//   _____        .__             
//  /  _  \_______|__|__ __ ______
// /  /_\  \_  __ |  |  |  /  ___/
///    |    |  | \|  |  |  \___ \ 
//\____|__  |__|  |__|____/____  >
//        \/                   \/ ";


            var versionUtc = DateTime.UtcNow;
            var logFilePath = $"arius-{versionUtc.ToString("o").Replace(":", "-")}.log";

            // Read config from appsettings.json -- https://stackoverflow.com/a/69057809/1582323
            var config = new ConfigurationBuilder()
                .AddJsonFile($"appsettings.json", true, true)
                .Build();

            var services = new ServiceCollection();
            services.AddAriusCore();
            //services.AddSingleton<IArchiveCommandOptionsProvider, ArchiveCliCommand>();
            services.AddLogging(builder =>
            {
                // Add Coconfnsole Logging
                //loggingBuilder
                //    .AddConfiguration(config.GetSection("Logging"))
                //    //.AddSimpleConsole(options =>
                //    //{
                //    //    // See for options: https://docs.microsoft.com/en-us/dotnet/core/extensions/console-log-formatter#simple
                //    //});
                //    .AddCustomFormatter(options => { });

                // Add File Logging
                // Do not log to file if we are in a unit test - Do not configure Karambola file logging in a unit test. The Karambola extension disposes itself in a weird way when the IHost is initialized multiple times in one ApplicationDomain during the test suite execution
                //if (!Environment.GetCommandLineArgs()[0].EndsWith("testhost.dll"))
                //{
                //    builder.AddFile(options =>
                //    {
                //        options.RootPath = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" && Directory.Exists("/logs") ? 
                //            "/logs" : 
                //            AppContext.BaseDirectory;

                //        options.Files = new[] { new LogFileOptions { Path = logFilePath } };

                //        options.TextBuilder = SingleLineLogEntryTextBuilder.Default;
                //    });

                //}
            });

            var registrar = new TypeRegistrar(services);

            var app = new CommandApp(registrar);
            app.Configure(config =>
            {
                config.SetInterceptor(new LogInterceptor()); // add the interceptor

                config.SetApplicationName("arius");

                config.PropagateExceptions();

                config.AddCommand<ArchiveCliCommand>("archive");

            });

            try
            {
                return app.Run(args);
            }
            catch (Exception e)
            {
                AnsiConsole.WriteException(e, ExceptionFormats.ShortenEverything);
                return -1;
            }
            
        }
    }
}