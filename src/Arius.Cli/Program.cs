using System;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Arius.Cli.Commands;
using Arius.Cli.Utils;
using Arius.Core.Facade;
using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using WouterVanRanst.Utils.Extensions;

[assembly: InternalsVisibleTo("Arius.Cli.Tests")]

namespace Arius.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await Main(args, null);
    }
    internal static async Task<int> Main(string[] args, Action<IServiceCollection> configureServices)
    {
        try
        {
            WriteAriusFiglet();

            // See https://github.com/patriksvensson/spectre.console-di-sample from https://github.com/spectreconsole/spectre.console/discussions/380#discussioncomment-2214455
            return await CreateHostBuilder(configureServices)
                .Build()
                .RunAsync(args);
        }
        catch (Exception e)
        {
            switch (e)
            {
                case CommandParseException cpe:
                    AnsiConsole.Write(cpe.Pretty);
                    break;
                case CommandRuntimeException cre: // occurs when ValidationResult.Error is returned in Validate()
                    AnsiConsole.Write(new Markup($"[bold red]Command error:[/] {cre.Message}"));
                    break;
                default:
                    AnsiConsole.Write(new Markup($"\n[bold red]Runtime Error:[/] {e.Message}. See log file for more information."));
                    break;
            }

            AnsiConsole.WriteLine();

            return -1;
        }
        finally
        {
            await FinalizeLogAsync();
        }
    }

    static Program()
    {
        IsRunningInContainer = Environment.GetEnvironmentVariable(DOTNET_RUNNING_IN_CONTAINER) == "true";
        IsUnitTest           = Environment.GetCommandLineArgs()[0].EndsWith("testhost.dll");
        LogDirectory         = GetLogDirectory();
        LogTimestamp         = DateTime.UtcNow;
        CommandName          = default;
        ContainerName        = default;

        static DirectoryInfo GetLogDirectory()
        {
            if (IsRunningInContainer)
            {
                if (!Directory.Exists("/logs"))
                    throw new InvalidOperationException("Running in container but Logs path is not defined");

                return new DirectoryInfo("/logs");
            }
            else
            {
                return new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, $"logs")).CreateIfNotExists();
            }
        }
    }

    internal const string        DOTNET_RUNNING_IN_CONTAINER             = "DOTNET_RUNNING_IN_CONTAINER";
    internal const string        AriusAccountNameEnvironmentVariableName = "ARIUS_ACCOUNT_NAME";
    internal const string        AriusAccountKeyEnvironmentVariableName  = "ARIUS_ACCOUNT_KEY";
    private static bool          IsRunningInContainer { get; }
    private static bool          IsUnitTest           { get; }
    private static DirectoryInfo LogDirectory         { get; }
    private static DateTime      LogTimestamp         { get; }

    internal static string? CommandName   { get; set; }
    internal static string? ContainerName { get; set; }

    [ThreadStatic]
    internal static readonly bool IsMainThread = true; //https://stackoverflow.com/a/55205660/1582323

    private static IHostBuilder CreateHostBuilder(Action<IServiceCollection> configureServices = null)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                // Add Console Logging
                logging.AddCustomFormatter();

                // Add File Logging
                // Do not log to file if we are in a unit test - Do not configure Karambola file logging in a unit test. The Karambola extension disposes itself in a weird way when the IHost is initialized multiple times in one ApplicationDomain during the test suite execution
                if (IsUnitTest)
                    return;

                logging.AddFile(options =>
                {
                    options.RootPath = LogDirectory.FullName;

                    options.Files =
                    [
                        new LogFileOptions
                        {
                            Path          = $"arius-{LogTimestamp.ToString("o").Replace(":", "-")}-<counter>.log",
                            CounterFormat = "00",
                            MaxFileSize   = 1024 * 1024 * 100 // 100 MB max file size
                        }
                    ];

                    options.TextBuilder = SingleLineLogEntryTextBuilder.Default;
                });


            })
            .ConfigureServices(services =>
            {
                // Register services here
                services.AddSingleton<Facade>();

                configureServices?.Invoke(services);

                // Add command line
                services.AddCommandLine(config =>
                {
                    config.SetApplicationName("arius");

                    config.AddCommand<ArchiveCliCommand>("archive");
                    config.AddCommand<RestoreCliCommand>("restore");
                    //config.AddCommand<RehydrateCliCommand>("rehydrate");

                    config.SetInterceptor(new CommandInterceptor(LogTimestamp));

                    // Propagate exceptions and use a custom exception handler
                    config.PropagateExceptions();
                });
            });
    }

    private static void WriteAriusFiglet()
    {
        AnsiConsole.Write(new FigletText(FigletFont.Default, "Arius").LeftJustified().Color(Color.Blue));
    }

    private static async Task FinalizeLogAsync()
    {
        var logfiles = new[] { LogDirectory, new DirectoryInfo(AppContext.BaseDirectory) }
            .SelectMany(di => di.GetFiles($"arius-{LogTimestamp.ToString("o").Replace(":", "-")}*.*"))
            .DistinctBy(fi => fi.FullName) //a bit h4x0r -- in Docker container, the DB is written to the AppContext.BaseDirectory but the log to /logs
            .ToArray();

        if (logfiles.None())
            return; // If there are no log files, return early

        AnsiConsole.Write("Compressing logs... ");

        var tarDir = LogDirectory.CreateSubdirectory(Path.GetRandomFileName()); // TODO: look at https://github.com/icsharpcode/SharpZipLib/wiki/GZip-and-Tar-Samples#createFull
        foreach (var lf in logfiles)
            lf.MoveTo(Path.Combine(tarDir.FullName, lf.Name));

        var logFileName = new StringBuilder("arius-");
        if (CommandName is not null)
            logFileName.Append($"{CommandName}-");
        if (ContainerName is not null)
            logFileName.Append($"{ContainerName}-");
        logFileName.Append($"{LogTimestamp.ToString("o").Replace(":", "-")}.tar");
        var tarfi = LogDirectory.GetFileInfo(logFileName.ToString());
        await TarFile.CreateFromDirectoryAsync(tarDir.FullName, tarfi.FullName, false);
        tarDir.Delete(recursive: true);

        await tarfi.CompressAsync(deleteOriginal: true);

        AnsiConsole.WriteLine($"done: {tarfi.FullName}.gzip");
    }
}