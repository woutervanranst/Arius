using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Cli.Commands;
using Arius.Cli.Utils;
using Arius.Core.Commands;
using Arius.Core.Extensions;
using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Arius.Cli;

public static class Program
{
    [ThreadStatic]
    public static readonly bool IsMainThread = true; //https://stackoverflow.com/a/55205660/1582323

    public static async Task<int> Main(string[] args)
    {
        AnsiConsole.Write(
            new FigletText(FigletFont.Default, "Arius")
                .LeftAligned()
                .Color(Color.Blue));

        // Read config from appsettings.json -- https://stackoverflow.com/a/69057809/1582323
        var config = new ConfigurationBuilder()
            .AddJsonFile($"appsettings.json", true, true)
            //.AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true)//To specify environment
            //.AddEnvironmentVariables();//
            .Build();

        var logPath = new DirectoryInfo(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" && Directory.Exists("/logs") ? "/logs" : AppContext.BaseDirectory);
        var versionUtc = DateTime.UtcNow;
        var logFileName = $"arius-{versionUtc.ToString("o").Replace(":", "-")}.log";
        
        var services = new ServiceCollection()
            .AddAriusCore()
            .AddSingleton(new StateVersion(versionUtc))
            //.AddSingleton<IConfigurationRoot>(config);
            .AddLogging(builder =>
            {
                builder.AddConfiguration(config.GetSection("Logging")); // if this doesnt work see https://stackoverflow.com/a/54892390/1582323, https://blog.bitscry.com/2017/05/30/appsettings-json-in-net-core-console-app/

                // Add Console Logging
                //.AddSimpleConsole(options =>
                //{
                //    // See for options: https://docs.microsoft.com/en-us/dotnet/core/extensions/console-log-formatter#simple
                //});
                builder.AddCustomFormatter(options => { });

                // Add File Logging
                // Do not log to file if we are in a unit test - Do not configure Karambola file logging in a unit test. The Karambola extension disposes itself in a weird way when the IHost is initialized multiple times in one ApplicationDomain during the test suite execution
                if (!Environment.GetCommandLineArgs()[0].EndsWith("testhost.dll"))
                {
                    builder.AddFile(options =>
                    {
                        config.GetSection("Logging").Bind("File", options);

                        options.RootPath = logPath.FullName;

                        options.Files = new[] { new LogFileOptions { Path = logFileName } };

                        options.TextBuilder = SingleLineLogEntryTextBuilder.Default;
                    });
                }
            });

        var registrar = new TypeRegistrar(services);
        var parsedOptionsProvider = new ParsedOptionsProvider();

        var app = new CommandApp(registrar);
        app.Configure(config =>
        {
            config.SetInterceptor(new CommandInterceptor(parsedOptionsProvider));

            config.SetApplicationName("arius");

            config.SetExceptionHandler(ex =>
            {
                var logger = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>().CreateLogger("Main");
                logger.LogError(ex);

                switch (ex)
                {
                    case CommandParseException e:
                        AnsiConsole.Write(e.Pretty);
                        break;
                    case CommandRuntimeException e: // occurs when ValidationResult.Error is returned in Validate()
                        AnsiConsole.Write(e.Message);
                        break;
                    default:
                        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                        break;
                }

                return -1;
            });

            config.AddCommand<ArchiveCliCommand>("archive");
            config.AddCommand<RestoreCliCommand>("restore");
            config.AddCommand<RehydrateCliCommand>("rehydrate");
        });

        var r = await app.RunAsync(args);

        // See https://github.com/spectreconsole/spectre.console/discussions/162
        //return await AnsiConsole.Progress()
        //    .Columns(new ProgressColumn[]
        //    {
        //        new TaskDescriptionColumn(),
        //        new SpinnerColumn()
        //    })
        //    .StartAsync(async context =>
        //    {
        //        var t0 = context.AddTask("archive");

        //        return await app.RunAsync(args);
        //    });
        

        var logfiles = new[] { logPath, new DirectoryInfo(AppContext.BaseDirectory) }
            .SelectMany(di => di.GetFiles($"arius-{versionUtc.ToString("o").Replace(":", "-")}.*"))
            .DistinctBy(fi => fi.FullName); //a bit h4x0r -- in Docker container, the DB is written to the AppContext.BaseDirectory but the log to /logs
        
        var ro = (IRepositoryOptions)parsedOptionsProvider.Options;
        var commandName = parsedOptionsProvider.Options switch
        {
            ArchiveCliCommand.ArchiveCommandOptions => "archive",
            RestoreCliCommand.RestoreCommandOptions => "restore",
            RehydrateCliCommand.RehydrateCommandOptions => "rehydrate",
            _ => throw new NotImplementedException()
        };

        foreach (var logfile in logfiles)
        {
            // prepend the container name to the log
            logfile.MoveTo(Path.Combine(logPath.FullName, logfile.Name.Replace("arius-", $"arius-{commandName}-{ro.Container}-")));

            AnsiConsole.WriteLine($"Compressing {logfile.Name}...");
            await logfile.CompressAsync(deleteOriginal: true);
            AnsiConsole.WriteLine($"Compressing {logfile.Name}... done");
        }

        return r;
    }
}