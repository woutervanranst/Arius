using Arius.Core;
using Arius.Core.Commands;
using Arius.Core.Models;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.Collections.Concurrent;

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
            .AutoRefresh(true)
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(new ProgressColumn[]
            {
                new ElapsedTimeColumn(),

                new TaskDescriptionColumn(),
                new SpinnerColumn(Spinner.Known.Dots),



                //new SpinnerColumn(Spinner.Known.Star),
                //new TaskDescriptionColumn(),
                //new ProgressBarColumn(),
                ////new PercentageColumn(),
                ////new RemainingTimeColumn(),
                ////new SpinnerColumn()
            })
            .StartAsync(async ctx =>
            {
                // We keep track of tasks by file name
                var taskDictionary = new ConcurrentDictionary<string, ProgressTask>();

                // 2. Create an IProgress<FileProgressUpdate> that the handler will call
                var progressUpdates = new Progress<FileProgressUpdate>(update =>
                {
                    // If this file is new, create a new task
                    var task = taskDictionary.GetOrAdd(update.FileName, fileName =>
                    {
                        // Create a Spectre Console progress task for this file
                        var t = ctx.AddTask($"[blue]{fileName}[/]", autoStart: true).IsIndeterminate(); //, autoStart: true).IsIndeterminate(); //, maxValue: 100, autoStart:true );
                        //t.IsIndeterminate = true;
                        //t.StartTask();
                        //t.IsIndeterminate = true;
                        //t.StartTask();
                        return t;
                    });

                    // Update the current progress percentage for this file
                    //task.Value = update.Percentage;

                    // Optionally display some extra status text in the description
                    if (!string.IsNullOrWhiteSpace(update.StatusMessage))
                    {
                        // E.g. "[blue]file.txt[/] (Reading... 50%)"
                        task.Description = $"[blue]{update.FileName}[/] ({update.StatusMessage})";
                    }

                    // If the file is complete, we can stop the task
                    if (update.Percentage >= 100)
                    {
                        task.StopTask();
                    }
                });

                // 3. Send the MediatR command and wait for completion
                // The handler will report progress via 'progressUpdates'
                var c = new ArchiveCommand
                {
                    //AccountName   = config.AccountName,
                    //AccountKey    = config.AccountKey,
                    //ContainerName = config.ContainerName ?? "atest",
                    //Passphrase    = config.Passphrase,
                    AccountName = "ariusci",
                    ContainerName = "atest",
                    Passphrase = "woutervr",
                    RemoveLocal = false,
                    Tier = StorageTier.Cool,
                    LocalRoot = new DirectoryInfo("C:\\Users\\RFC430\\Downloads\\New folder"),

                    ProgressReporter = progressUpdates
                };

                await mediator.Send(c);

                // Once the handler completes, all updates should have been reported
                AnsiConsole.MarkupLine("[green]All files processed![/]");
            });
    }
}