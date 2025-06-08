using Arius.Core.Commands;
using Arius.Core.Models;
using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using MediatR;
using Spectre.Console;
using System.Collections.Concurrent;

namespace Arius.Cli;

[Command("archive", Description = "Archives a local directory to Azure Blob Storage.")]
public sealed class ArchiveCliCommand : ICommand
{
    private readonly IMediator _mediator;

    public ArchiveCliCommand(IMediator mediator)
    {
        _mediator = mediator;
    }

    [CommandParameter(0, Description = "Path to the local root directory to archive.")]
    public required DirectoryInfo LocalRoot { get; init; }

    [CommandOption("accountname", IsRequired = true, Description = "Azure Storage Account name.", EnvironmentVariable = "ARIUS_ACCOUNT_NAME")]
    public required string AccountName { get; init; }

    [CommandOption("accountkey", IsRequired = true, Description = "Azure Storage Account key.", EnvironmentVariable = "ARIUS_ACCOUNT_KEY")]
    public required string AccountKey { get; init; }

    [CommandOption("passphrase", IsRequired = true, Description = "Passphrase for encryption.")]
    public required string Passphrase { get; init; }

    [CommandOption("container", IsRequired = true, Description = "Azure Blob Storage container name.")]
    public required string ContainerName { get; init; }

    [CommandOption("remove-local", Description = "Remove local files after a successful upload.")]
    public bool RemoveLocal { get; init; } = false;

    [CommandOption("tier", Description = "Storage tier for the uploaded blobs.")]
    public StorageTier Tier { get; init; } = StorageTier.Cool;

    public async ValueTask ExecuteAsync(IConsole console)
    {
        AnsiConsole.Write(
            new FigletText("Arius")
                .LeftJustified()
                .Color(Color.Red));

        try
        {
            await AnsiConsole.Progress()
                .AutoRefresh(true)
                .AutoClear(false)
                .HideCompleted(true)
                .Columns(
                    new ElapsedTimeColumn(),
                    new ProgressBarColumn(),
                    new TaskDescriptionColumn { Alignment = Justify.Right }
                )
                .StartAsync(async ctx =>
                {
                    var queue = new ConcurrentQueue<ProgressUpdate>();
                    var pu = new Progress<ProgressUpdate>(u => queue.Enqueue(u));

                    // Create the MediatR command from the CLI arguments
                    var command = new ArchiveCommand
                    {
                        AccountName      = this.AccountName,
                        AccountKey       = this.AccountKey,
                        ContainerName    = this.ContainerName,
                        Passphrase       = this.Passphrase,
                        RemoveLocal      = this.RemoveLocal,
                        Tier             = this.Tier,
                        LocalRoot        = this.LocalRoot,
                        ProgressReporter = pu
                    };

                    // Send the command and start the progress display loop
                    var commandTask = _mediator.Send(command);

                    var taskDictionary = new ConcurrentDictionary<string, ProgressTask>();

                    while (!commandTask.IsCompleted)
                    {
                        while (queue.TryDequeue(out var u))
                        {
                            // Handle different types of progress updates
                            if (u is TaskProgressUpdate tpu)
                            {
                                var task = taskDictionary.GetOrAdd(tpu.TaskName, taskName => ctx.AddTask($"[blue]{taskName}[/]").IsIndeterminate());
                                if (!string.IsNullOrWhiteSpace(tpu.StatusMessage))
                                    task.Description = $"[blue]{tpu.TaskName}[/] ({tpu.StatusMessage})";
                                if (tpu.Percentage >= 100)
                                    task.StopTask();
                            }
                            else if (u is FileProgressUpdate fpu)
                            {
                                var task = taskDictionary.GetOrAdd(fpu.FileName, fileName => ctx.AddTask($"[blue]{fileName}[/]"));
                                task.Description = $"[blue]{TruncateAndRightJustify(fpu.FileName, 50)}[/] ({TruncateAndLeftJustify(fpu.StatusMessage, 20)})";
                                task.Value = fpu.Percentage;
                                if (fpu.Percentage >= 100)
                                    task.StopTask();
                            }
                            else
                            {
                                AnsiConsole.MarkupLine($"[yellow]Unknown progress update type: {u.GetType().Name}[/]");
                            }
                        }
                        await Task.Delay(100); // Prevent a tight loop from consuming 100% CPU
                    }

                    await commandTask; // Propagate any exceptions from the command handler

                    AnsiConsole.MarkupLine("[green]All files processed![/]");
                });
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e, ExceptionFormats.ShortenEverything);
        }
    }

    // --- Helper methods moved from Program.cs ---

    private static string TruncateAndRightJustify(string input, int width)
    {
        if (width <= 0) return string.Empty;
        const string ellipsis = "...";
        int contentWidth = width - ellipsis.Length;
        if (contentWidth <= 0) return ellipsis[..width];
        string truncated = input.Length > contentWidth ? ellipsis + input[^contentWidth..] : input;
        return truncated.PadLeft(width);
    }

    private static string TruncateAndLeftJustify(string input, int width)
    {
        if (width <= 0) return string.Empty;
        string truncated = input.Length > width ? input[..width] : input;
        return truncated.PadRight(width);
    }
}