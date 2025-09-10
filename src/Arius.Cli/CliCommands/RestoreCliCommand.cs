using Arius.Core.Features.Restore;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using Mediator;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace Arius.Cli.CliCommands;

public abstract class RestoreCliCommandBase : CliFx.ICommand
{
    private readonly IMediator                      mediator;
    private readonly ILogger<RestoreCliCommandBase> logger;

    public RestoreCliCommandBase(IMediator mediator, ILogger<RestoreCliCommandBase> logger)
    {
        this.mediator = mediator;
        this.logger   = logger;
    }

    [CommandOption("accountname", 'n', IsRequired = true, Description = "Azure Storage Account name.", EnvironmentVariable = "ARIUS_ACCOUNT_NAME")]
    public required string AccountName { get; init; }

    [CommandOption("accountkey", 'k', IsRequired = true, Description = "Azure Storage Account key.", EnvironmentVariable = "ARIUS_ACCOUNT_KEY")]
    public required string AccountKey { get; init; }

    [CommandOption("container", 'c', IsRequired = true, Description = "Azure Storage container name.")]
    public required string ContainerName { get; init; }

    [CommandOption("passphrase", 'p', IsRequired = true, Description = "Passphrase for decryption.")]
    public required string Passphrase { get; init; }

    public abstract DirectoryInfo LocalRoot { get; init; }

    [CommandParameter(0, Description = "Directory or files to restore.", IsRequired = false)]
    public string[] Targets { get; init; } = ["./"];

    [CommandOption("download", Description = "Download the files.")]
    public bool Download { get; init; } = false;

    [CommandOption("include-pointers", Description = "Create respective pointer files alongside the binaries.")]
    public bool IncludePointers { get; init; } = false;

    public async ValueTask ExecuteAsync(IConsole console)
    {
        try
        {
            AnsiConsole.Write(
                new FigletText("Arius")
                    .LeftJustified()
                    .Color(Color.Red));

            await AnsiConsole.Progress()
                .AutoRefresh(true)
                .AutoClear(false)
                .HideCompleted(true)
                .Columns(
                    new ElapsedTimeColumn(),
                    new ProgressBarColumn(),
                    new TaskDescriptionColumn { Alignment = Justify.Right })
                .StartAsync(async ctx =>
                {
                    var progressUpdates = new ConcurrentQueue<ProgressUpdate>();

                    // Create the Mediator command from the CLI arguments
                    var command = new RestoreCommand
                    {
                        AccountName     = AccountName,
                        AccountKey      = AccountKey,
                        ContainerName   = ContainerName,
                        Passphrase      = Passphrase,
                        LocalRoot       = LocalRoot,
                        Targets         = Targets,
                        Download        = Download,
                        IncludePointers = IncludePointers,
                        ProgressReporter = new Progress<ProgressUpdate>(u => progressUpdates.Enqueue(u))
                    };

                    // Start the restore command in the background
                    var cancellationToken = console.RegisterCancellationHandler();
                    var commandTask       = mediator.Send(command, cancellationToken);

                    var taskDictionary = new ConcurrentDictionary<string, ProgressTask>();

                    while (!commandTask.IsCompleted && !progressUpdates.IsEmpty)
                    {
                        while (progressUpdates.TryDequeue(out var u))
                        {
                            // Handle different types of progress updates
                            if (u is TaskProgressUpdate tpu)
                            {
                                var task = taskDictionary.GetOrAdd(tpu.TaskName, taskName => ctx.AddTask($"[cyan1]{taskName}[/]").IsIndeterminate());
                                if (!string.IsNullOrWhiteSpace(tpu.StatusMessage))
                                    task.Description = $"[cyan1]{tpu.TaskName}[/] ({tpu.StatusMessage})";
                                task.Value = tpu.Percentage;
                                if (tpu.Percentage >= 100)
                                    task.StopTask();
                            }
                            else if (u is FileProgressUpdate fpu)
                            {
                                var task = taskDictionary.GetOrAdd(fpu.FileName, fileName => ctx.AddTask($"[cyan3]{fileName}[/]"));
                                task.Description = $"[cyan3]{fpu.FileName.TruncateAndRightJustify(50)}[/] ({fpu.StatusMessage?.TruncateAndLeftJustify(20)})";
                                task.Value       = fpu.Percentage;
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
            logger.LogError(e, "Unhandled exception");
            throw new CommandException(e.Message, showHelp: false, innerException: e);
        }
    }
}

[Command("restore", Description = "Restores a directory from Azure Blob Storage.")]
public class RestoreCliCommand : RestoreCliCommandBase
{
    public RestoreCliCommand(IMediator mediator, ILogger<RestoreCliCommand> logger) : base(mediator, logger)
    {
    }

    [CommandOption("root", 'r', Description = "Root directory for restore operation.")]
    public override DirectoryInfo LocalRoot { get; init; } = new(Environment.CurrentDirectory);
}



[Command("restore", Description = "Restores a directory from Azure Blob Storage. [Docker]")]
public class RestoreDockerCliCommand : RestoreCliCommandBase
{
    public RestoreDockerCliCommand(IMediator mediator, ILogger<RestoreDockerCliCommand> logger) : base(mediator, logger)
    {
    }

    public override required DirectoryInfo LocalRoot
    {
        get => new("/archive");
        init => throw new InvalidOperationException("LocalRoot cannot be set in Docker");
    }
}