using Arius.Cli.Commands;
using Arius.Cli.Utils;
using Arius.Core.Commands;
using Arius.Core.Extensions;
using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx.Synchronous;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using WouterVanRanst.Utils.Extensions;

[assembly: InternalsVisibleTo("Arius.Cli.Tests")]

namespace Arius.Cli;

public class Program
{
    public static int Main(string[] args)
    {
        return InternalMain(args).ExitCode;
    }

    internal static (int ExitCode, ICommandOptions? ParsedOptions, Exception? Exception) InternalMain(string[] args, Action<IServiceCollection>? addAriusCoreCommandsProvider = null)
    {
        WriteFiglet();

        var versionUtc = DateTime.UtcNow;
        var logDirectory = GetLogDirectory(versionUtc);

        // See https://github.com/patriksvensson/spectre.console-di-sample from https://github.com/spectreconsole/spectre.console/discussions/380#discussioncomment-2214455
        var h = CreateHostBuilder(addAriusCoreCommandsProvider, logDirectory, versionUtc).Build();

        try
        {
            var exitCode = h.Run(args);

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

            Trace.Assert(exitCode == 0, "ExitCode is not 0 but should be. Where did the error go?");

            return (exitCode, GetParsedOptions(h), null);
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
                    AnsiConsole.Write(new Markup($"\n[bold red]Runtime Error:[/] {e.Message} See log file for more information."));
                    break;
            }

            AnsiConsole.WriteLine();

            return (-1, GetParsedOptions(h), e);
        }
        finally
        {
            FinalizeLog(GetParsedOptions(h), logDirectory, versionUtc).WaitAndUnwrapException(); // See https://stackoverflow.com/a/9343733/1582323
        }

        static ICommandOptions? GetParsedOptions(IHost h)
        {
            return ((CommandInterceptor)h.Services.GetRequiredService<ICommandInterceptor>()).ParsedOptions;
        }

        static string GetLogDirectory(DateTime versionUtc)
        {
            string path;

            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            {
                if (!Directory.Exists("/logs"))
                    throw new InvalidOperationException("Running in container but Logs path is not defined");

                path = "/logs";
            }
            else
            {
                path = Path.Combine(AppContext.BaseDirectory, $"logs"/*, $"{versionUtc.ToString("o").Replace(":", "-")}"*/);
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }

            return path;
        }
    }

    private static IHostBuilder CreateHostBuilder(Action<IServiceCollection>? addAriusCoreCommandsProvider, string logDirectory, DateTime versionUtc)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                // Configure logging

                // Add Console Logging
                //.AddSimpleConsole(options =>
                //{
                //    // See for options: https://docs.microsoft.com/en-us/dotnet/core/extensions/console-log-formatter#simple
                //});
                logging.AddCustomFormatter(options => { });

                // Add File Logging
                // Do not log to file if we are in a unit test - Do not configure Karambola file logging in a unit test. The Karambola extension disposes itself in a weird way when the IHost is initialized multiple times in one ApplicationDomain during the test suite execution
                if (Environment.GetCommandLineArgs()[0].EndsWith("testhost.dll"))
                    return;

                // Add File Logging
                logging.AddFile(options =>
                {
                    options.RootPath = logDirectory;

                    options.Files = new[] { new LogFileOptions 
                    { 
                        Path = $"arius-{versionUtc.ToString("o").Replace(":", "-")}-<counter>.log", 
                        CounterFormat = "00",
                        MaxFileSize = 1024 * 1024 * 100 // 100 MB max file size
                    } };

                    options.TextBuilder = SingleLineLogEntryTextBuilder.Default;
                });
            })
            .ConfigureServices(services =>
            {
                if (addAriusCoreCommandsProvider is not null)
                    addAriusCoreCommandsProvider(services);
                else
                    services.AddAriusCoreCommands();

                var interceptor = new CommandInterceptor(versionUtc);
                services.AddSingleton<ICommandInterceptor>(interceptor);

                // Add command lines
                services.AddCommandLine(config =>
                {
                    config.SetApplicationName("arius");

                    config.AddCommand<ArchiveCliCommand>("archive");
                    config.AddCommand<RestoreCliCommand>("restore");
                    //config.AddCommand<RehydrateCliCommand>("rehydrate");

                    config.SetInterceptor(interceptor);

                    // Do not propagate exceptions - rather use a custom exception handler
                    config.PropagateExceptions();
                });
            });
    }

    internal const string AriusAccountNameEnvironmentVariableName = "ARIUS_ACCOUNT_NAME";
    internal const string AriusAccountKeyEnvironmentVariableName = "ARIUS_ACCOUNT_KEY";
    
    [ThreadStatic]
    internal static readonly bool IsMainThread = true; //https://stackoverflow.com/a/55205660/1582323
    
    private static void WriteFiglet()
    {
        AnsiConsole.Write(
            new FigletText(FigletFont.Default, "Arius")
                .LeftAligned()
                .Color(Color.Blue));
    }

    private static async Task FinalizeLog(ICommandOptions? po, string logDirectoryPath, DateTime versionUtc)
    {
        var logDirectory = new DirectoryInfo(logDirectoryPath);
        var lfs = new[] { logDirectory, new DirectoryInfo(AppContext.BaseDirectory) }
            .SelectMany(di => di.GetFiles($"arius-{versionUtc.ToString("o").Replace(":", "-")}*.*"))
            .DistinctBy(fi => fi.FullName) //a bit h4x0r -- in Docker container, the DB is written to the AppContext.BaseDirectory but the log to /logs
            .ToArray();
        
        if (!lfs.Any()) // If there are no log files, return early
            return;

        AnsiConsole.Write($"Compressing logs... ");

        var tarDir = logDirectory.CreateSubdirectory(Path.GetRandomFileName());
        foreach (var lf in lfs)
            lf.MoveTo(Path.Combine(tarDir.FullName, lf.Name));
        
        var tarfi = GetTarFileInfo(po, versionUtc, logDirectory);
        TarFile.CreateFromDirectory(tarDir.FullName, tarfi.FullName, false);
        tarDir.Delete(recursive: true);

        await tarfi.CompressAsync(deleteOriginal: true);

        AnsiConsole.WriteLine($"done: {tarfi.FullName}.gzip");

        static FileInfo GetTarFileInfo(ICommandOptions? po, DateTime versionUtc, DirectoryInfo logDirectory)
        {
            var tarFileNameBuilder = new StringBuilder("arius-");
            if (GetCommandName(po) is var commandName && commandName is not null)
                tarFileNameBuilder.Append($"{commandName}-");
            if (GetContainerName(po) is var containerName && containerName is not null)
                tarFileNameBuilder.Append($"{containerName}-");
            tarFileNameBuilder.Append($"{versionUtc.ToString("o").Replace(":", "-")}.tar");
            
            var tarFile = new FileInfo(Path.Combine(logDirectory.FullName, tarFileNameBuilder.ToString()));
            
            return tarFile;

            static string? GetCommandName(ICommandOptions? po) => po switch
            {
                ArchiveCliCommand.ArchiveCommandOptions => "archive",
                RestoreCliCommand.RestoreCommandOptions => "restore",
                // RehydrateCliCommand.RehydrateCommandOptions => "rehydrate",
                null => null,
                _ => throw new NotImplementedException()
            };

            static string? GetContainerName(ICommandOptions? po) => ((IRepositoryOptions?)po)?.Container;
        }
    }
}