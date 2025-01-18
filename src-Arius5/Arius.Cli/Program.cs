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

        await AnsiConsole.Status()
            .StartAsync("Initializing...", async ctx =>
            {
                // Create a progress reporter for the files being processed
                var progressReporter = new Progress<string>(message =>
                {
                    // Update the overall status text
                    ctx.Status($"[cyan]{message}[/]");

                    // Also write a line to the console
                    AnsiConsole.MarkupLine(message);

                    // If you want the spinner/status to update immediately
                    ctx.Refresh();
                });

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
            });
    }
}