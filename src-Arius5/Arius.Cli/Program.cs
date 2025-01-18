using Arius.Core;
using Arius.Core.Commands;
using Arius.Core.Models;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Arius.Cli;

internal class Program
{
    static async Task Main(string[] args)
    {
        var services = new ServiceCollection()
            .AddArius(c => { })
            .AddLogging()
            .BuildServiceProvider();

        var mediator = services.GetRequiredService<IMediator>();

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                // Create a single task (or multiple tasks if needed)
                var task = ctx.AddTask("[green]Executing work...[/]", autoStart: false);

                // We'll report increments to this IProgress<double>
                var progressReporter = new Progress<double>(value =>
                {
                    // Increment the Spectre.Console task by the reported value
                    task.Increment(value);
                });

                // Start the progress task
                task.StartTask();

                // Execute the MediatR command and pass in our progress reporter
                var c = new ArchiveCommand
                {
                    //AccountName   = config.AccountName,
                    //AccountKey    = config.AccountKey,
                    //ContainerName = config.ContainerName ?? "atest",
                    //Passphrase    = config.Passphrase,
                    AccountName   = "ariusci",
                    ContainerName = "atest",
                    Passphrase    = "woutervr",
                    RemoveLocal   = false,
                    Tier          = StorageTier.Cool,
                    LocalRoot     = new DirectoryInfo("C:\\Users\\RFC430\\Downloads\\New folder"),

                    ProgressReporter = progressReporter
                };

                await mediator.Send(c);

                task.StopTask();
            });
    }
}