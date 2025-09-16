using Arius.Core.Features.Archive;
using Arius.Core.Shared.Storage;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using Mediator;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Arius.Cli.CliCommands;

public abstract class ArchiveCliCommandBase : CliFx.ICommand
{
    private readonly IMediator                      mediator;
    private readonly ILogger<ArchiveCliCommandBase> logger;

    public ArchiveCliCommandBase(IMediator mediator, ILogger<ArchiveCliCommandBase> logger)
    {
        this.mediator   = mediator;
        this.logger = logger;
    }

    public abstract DirectoryInfo LocalRoot { get; init; }

    [CommandOption("accountname", 'n', IsRequired = true, Description = "Azure Storage Account name.", EnvironmentVariable = "ARIUS_ACCOUNT_NAME")]
    public required string AccountName { get; init; }

    [CommandOption("accountkey", 'k', IsRequired = true, Description = "Azure Storage Account key.", EnvironmentVariable = "ARIUS_ACCOUNT_KEY")]
    public required string AccountKey { get; init; }

    [CommandOption("container", 'c', IsRequired = true, Description = "Azure Blob Storage container name.")]
    public required string ContainerName { get; init; }
    
    [CommandOption("passphrase", 'p', IsRequired = true, Description = "Passphrase for encryption.")]
    public required string Passphrase { get; init; }

    [CommandOption("tier", Description = "Storage tier for the uploaded blobs.")]
    public StorageTier Tier { get; init; } = StorageTier.Archive;

    [CommandOption("remove-local", Description = "Remove local files after a successful upload.")]
    public bool RemoveLocal { get; init; } = false;


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
                    new TaskDescriptionColumn { Alignment = Justify.Right }
                )
                .StartAsync(async ctx =>
                {
                    var progressUpdates = Channel.CreateUnbounded<ProgressUpdate>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false });

                    // Create the Mediator command from the CLI arguments
                    var command = new ArchiveCommand
                    {
                        AccountName      = AccountName,
                        AccountKey       = AccountKey,
                        ContainerName    = ContainerName,
                        Passphrase       = Passphrase,
                        RemoveLocal      = RemoveLocal,
                        Tier             = Tier,
                        LocalRoot        = LocalRoot,
                        ProgressReporter = new Progress<ProgressUpdate>(u => progressUpdates.Writer.TryWrite(u))
                    };

                    // Send the command and start the progress display loop
                    var cancellationToken = console.RegisterCancellationHandler();
                    var commandTask       = mediator.Send(command, cancellationToken).AsTask();
                    commandTask.ContinueWith(_ => progressUpdates.Writer.Complete());

                    var taskDictionary = new ConcurrentDictionary<string, ProgressTask>();

                    // Process progress updates as they arrive
                    await foreach (var u in progressUpdates.Reader.ReadAllAsync(cancellationToken))
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
                            task.Description = $"[blue]{fpu.FileName.EscapeMarkup().TruncateAndRightJustify(50)}[/] ({fpu.StatusMessage?.TruncateAndLeftJustify(20)})";
                            task.Value       = fpu.Percentage;
                            if (fpu.Percentage >= 100)
                                task.StopTask();
                        }
                        else
                        {
                            AnsiConsole.MarkupLine($"[yellow]Unknown progress update type: {u.GetType().Name}[/]");
                        }
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
        //catch (ValidationException e)
        //{
        //    throw new CommandException(e.Message, showHelp: true);
        //}
        //catch (Exception e)
        //{
        //    AnsiConsole.WriteException(e, ExceptionFormats.ShortenEverything);
        //}
    }
}

[Command("archive", Description = "Archives a local directory to Azure Blob Storage.")]
public class ArchiveCliCommand: ArchiveCliCommandBase
{
    public ArchiveCliCommand(IMediator mediator, ILogger<ArchiveCliCommand> logger) : base(mediator, logger)
    {
    }

    [CommandParameter(0, Description = "Path to the local root directory to archive.")]
    public override required DirectoryInfo LocalRoot { get; init; }
}



[Command("archive", Description = "Archives a local directory to Azure Blob Storage. [Docker]")]
public class ArchiveDockerCliCommand : ArchiveCliCommandBase
{
    public ArchiveDockerCliCommand(IMediator mediator, ILogger<ArchiveDockerCliCommand> logger) : base(mediator, logger)
    {
    }

    public override required DirectoryInfo LocalRoot
    {
        get => new("/archive");
        init => throw new InvalidOperationException("LocalRoot cannot be set in Docker");
    }
}
