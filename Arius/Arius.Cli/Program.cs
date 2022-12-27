using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
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

[assembly: InternalsVisibleTo("Arius.Cli.Tests")]

namespace Arius.Cli;

public class Program
{
    internal const string AriusAccountNameEnvironmentVariableName = "ARIUS_ACCOUNT_NAME";
    internal const string AriusAccountKeyEnvironmentVariableName = "ARIUS_ACCOUNT_KEY";
    
    internal static Program? Instance { get; set; }

    [ThreadStatic]
    internal static readonly bool IsMainThread = true; //https://stackoverflow.com/a/55205660/1582323
    
    
    internal ICommandOptions? ParsedOptions { get; set; }

    internal Exception? e;

    public static async Task<int> Main(string[] args)
    {
        return await new Program().Main(args, sc => sc.AddAriusCoreCommands());
    }

    internal async Task<int> Main(string[] args, Action<IServiceCollection> addAriusCoreCommandsProvider)
    {
        Instance = this;
        
        WriteFiglet();

        IConfigurationRoot config = BuildConfiguration();

        var logPath = new DirectoryInfo(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" && Directory.Exists("/logs") ? "/logs" : AppContext.BaseDirectory);
        var versionUtc = DateTime.UtcNow;
        var logFileName = $"arius-{versionUtc.ToString("o").Replace(":", "-")}.log";

        IServiceCollection services = BuildServiceCollection(addAriusCoreCommandsProvider, config, logFileName);

        var registrar = new TypeRegistrar(services);

        var app = new CommandApp(registrar);
        ConfigureServices(app, services);

        var r = app.Run(args);
        await FinalizeLog(logPath, versionUtc);

        return r;

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
    }

    private static void WriteFiglet()
    {
        AnsiConsole.Write(
            new FigletText(FigletFont.Default, "Arius")
                .LeftAligned()
                .Color(Color.Blue));
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        // Read config from appsettings.json -- https://stackoverflow.com/a/69057809/1582323
        var config = new ConfigurationBuilder()
            .AddJsonFile($"appsettings.json", true, true)
            //.AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true)//To specify environment
            //.AddEnvironmentVariables()
            .Build();
        return config;
    }

    private static IServiceCollection BuildServiceCollection(Action<IServiceCollection> addAriusCoreCommandsProvider, IConfigurationRoot config, string logFileName)
    {
        var sc = new ServiceCollection()
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

                        options.RootPath = new FileInfo(logFileName).Directory.FullName;

                        options.Files = new[] { new LogFileOptions { Path = logFileName } };

                        options.TextBuilder = SingleLineLogEntryTextBuilder.Default;

                        //options.FileAccessMode = LogFileAccessMode.OpenTemporarily;
                    });
                }
            });
        addAriusCoreCommandsProvider(sc);
        return sc;
    }

    private static void ConfigureServices(CommandApp app, IServiceCollection services)
    {
        app.Configure(config =>
        {
            config.SetApplicationName("arius");

            config.AddCommand<ArchiveCliCommand>("archive");
            config.AddCommand<RestoreCliCommand>("restore");
            //config.AddCommand<RehydrateCliCommand>("rehydrate");

            config.SetInterceptor(new CommandInterceptor());

            config.SetExceptionHandler(e =>
            {
                HandleError(services, e);
            });

            //config.PropagateExceptions();
        });
    }

    private static void HandleError(IServiceCollection services, Exception e)
    {
        Program.Instance.e = e;

        //var logger = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>().CreateLogger("CliMainExceptionLog");
        //logger.LogError(e);

        switch (e)
        {
            case CommandParseException cpe:
                AnsiConsole.Write(cpe.Pretty);
                break;
            //case CommandRuntimeException cre: // occurs when ValidationResult.Error is returned in Validate()
            //    AnsiConsole.WriteLine("Command error: " + cre.Message);
            //    break;
            default:
                AnsiConsole.Write(new Markup($"[bold red]Error:[/] {e.Message}\nSee log for more information."));
                break;
        }

        AnsiConsole.WriteLine();
    }

    private static async Task FinalizeLog(DirectoryInfo logPath, DateTime versionUtc)
    {
        var commandName = Program.Instance.ParsedOptions switch
        {
            ArchiveCliCommand.ArchiveCommandOptions => "archive",
            RestoreCliCommand.RestoreCommandOptions => "restore",
            // RehydrateCliCommand.RehydrateCommandOptions => "rehydrate",
            _ => throw new NotImplementedException()
        };

        var logfiles = new[] { logPath, new DirectoryInfo(AppContext.BaseDirectory) }
            .SelectMany(di => di.GetFiles($"arius-{versionUtc.ToString("o").Replace(":", "-")}.*"))
            .DistinctBy(fi => fi.FullName); //a bit h4x0r -- in Docker container, the DB is written to the AppContext.BaseDirectory but the log to /logs

        var containerName = ((IRepositoryOptions)Program.Instance.ParsedOptions).Container;

        foreach (var logfile in logfiles)
        {
            // prepend the container name to the log
            logfile.MoveTo(Path.Combine(logPath.FullName, logfile.Name.Replace("arius-", $"arius-{commandName}-{containerName}-")));

            AnsiConsole.WriteLine($"Compressing {logfile.Name}...");
            await logfile.CompressAsync(deleteOriginal: true);
            AnsiConsole.WriteLine($"Compressing {logfile.Name}... done");
        }
    }
}