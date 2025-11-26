using Arius.Core.Features.Commands.Archive;
using Arius.Core.Shared.Storage;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using Humanizer;
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

            var result = await AnsiConsole.Progress()
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

                    /* NOTE: There is no 'elegant/compact' way to reduce the verbosity of the AnsiConsole.Progress updates here, given the vertically sliced Commands.
                     * The best alternative is with a ProgressPump<T> but is not a code decrease. See https://chatgpt.com/s/t_68dd7739bea48191ab058d5680d8c438
                     */

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
                            var isError = tpu.Percentage < 0;
                            var color = isError ? "red" : "blue";
                            var task = taskDictionary.GetOrAdd(tpu.TaskName, taskName => ctx.AddTask($"[{color}]{taskName}[/]").IsIndeterminate());
                            if (!string.IsNullOrWhiteSpace(tpu.StatusMessage))
                                task.Description = $"[{color}]{tpu.TaskName}[/] ({tpu.StatusMessage})";
                            if (tpu.Percentage >= 100/* || isError*/)
                                task.StopTask();
                        }
                        else if (u is FileProgressUpdate fpu)
                        {
                            var isError = fpu.Percentage < 0;
                            var color = isError ? "red" : "blue";
                            var task = taskDictionary.GetOrAdd(fpu.FileName, fileName => ctx.AddTask($"[{color}]{fileName}[/]"));
                            task.Description = $"[{color}]{fpu.FileName.EscapeMarkup().TruncateAndRightJustify(50)}[/] ({fpu.StatusMessage?.TruncateAndLeftJustify(20)})";
                            task.Value       = isError ? 0 : fpu.Percentage;
                            if (fpu.Percentage >= 100/* || isError*/)
                                task.StopTask();
                        }
                        else
                        {
                            AnsiConsole.MarkupLine($"[yellow]Unknown progress update type: {u.GetType().Name}[/]");
                        }
                    }

                    return await commandTask; // Propagate any exceptions from the command handler
                });

            if (result.IsSuccess)
            {
                var s = result.Value;

                RenderArchiveTable(
                    $"{s.TotalLocalFiles}",      "?",                                                  "?",
                    "?",                         "?",                                                  "?",
                    "?",                         $"+{s.BytesUploadedUncompressed.Bytes().Humanize()}", "?",
                    $"{s.ExistingPointerFiles}", $"+{s.PointerFilesCreated}",                          "?",

                    "?",                                                                         "?",                                                                        "?",
                    $"{s.BinariesBeforeOperation} binaries in {s.ChunksBeforeOperation} chunks", $"+{s.UniqueBinariesUploaded} binaries in {s.UniqueChunksUploaded} chunks", $"{s.BinariesAfterOperation} binaries in {s.ChunksAfterOperation} chunks",
                    $"{s.ArchivedSizeBeforeOperation.Bytes().Humanize()}",                        $"+{s.BytesUploadedCompressed.Bytes().Humanize()}",                         $"{s.ArchivedSizeAfterOperation.Bytes().Humanize()}",
                    "?",                                                                         $"+ ? - {s.PointerFileEntriesDeleted}",                                     "?"
                );

                AnsiConsole.MarkupLine("[green]Archive completed successfully![/]");
                AnsiConsole.MarkupLine($"[green]Total files scanned: {s.TotalLocalFiles}, {s.UniqueBinariesUploaded} unique uploaded, in {s.UniqueChunksUploaded} chunks[/]");
                AnsiConsole.MarkupLine($"[green]Bytes uploaded: {s.BytesUploadedUncompressed.Bytes().Humanize()} -> {s.BytesUploadedCompressed.Bytes().Humanize()} (compression: {(s.BytesUploadedUncompressed > 0 ? ((double)s.BytesUploadedCompressed / s.BytesUploadedUncompressed).ToString("P1") : "N/A")})[/]");
                if (s.NewStateName is not null)
                    AnsiConsole.MarkupLine($"[green]State file uploaded: {s.NewStateName}[/]");
            }
            else
            {
                var errorMessage = string.Join("; ", result.Errors.Select(e => e.Message));
                AnsiConsole.MarkupLine($"[red]Archive operation failed: {errorMessage.EscapeMarkup()}[/]");
                throw new CommandException(errorMessage, showHelp: false);
            }
        }
        catch (CommandException)
        {
            throw;
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

    private void RenderArchiveTable(
        // Local (Before, Operation, After) × 4 rows
        string localFilesBefore, string localFilesOp, string localFilesAfter,
        string localBinariesBefore, string localBinariesOp, string localBinariesAfter,
        string localSizeBefore, string localSizeOp, string localSizeAfter,
        string localPointersBefore, string localPointersOp, string localPointersAfter,

        // Remote (Before, Operation, After) × 4 rows
        string remoteFilesBefore, string remoteFilesOp, string remoteFilesAfter,
        string remoteBinariesBefore, string remoteBinariesOp, string remoteBinariesAfter,
        string remoteSizeBefore, string remoteSizeOp, string remoteSizeAfter,
        string remotePointersBefore, string remotePointersOp, string remotePointersAfter
    )
    {
        var table = new Table().Border(TableBorder.None);

        Table MakeSubTable(string title)
        {
            var sub = new Table()
                .AddColumn(new TableColumn("[grey]Before[/]").Centered())
                .AddColumn(new TableColumn("[grey]Operation[/]").Centered())
                .AddColumn(new TableColumn("[grey]After[/]").Centered());
            sub.Title(title);
            return sub;
        }

        var local  = MakeSubTable("Local");
        var remote = MakeSubTable("Remote");

        local.AddRow(localFilesBefore,    localFilesOp,    localFilesAfter);
        local.AddRow(localBinariesBefore, localBinariesOp, localBinariesAfter);
        local.AddRow(localSizeBefore,     localSizeOp,     localSizeAfter);
        local.AddRow(localPointersBefore, localPointersOp, localPointersAfter);

        remote.AddRow(remoteFilesBefore,    remoteFilesOp,    remoteFilesAfter);
        remote.AddRow(remoteBinariesBefore, remoteBinariesOp, remoteBinariesAfter);
        remote.AddRow(remoteSizeBefore,     remoteSizeOp,     remoteSizeAfter);
        remote.AddRow(remotePointersBefore, remotePointersOp, remotePointersAfter);

        var legend = new Table()
            .Border(TableBorder.None)
            .AddColumn("")
            .AddRow("")
            .AddRow("")
            .AddRow("")
            .AddRow("[bold]Files[/]")
            .AddRow("[bold]Binaries[/]")
            .AddRow("[bold]Size[/]")
            .AddRow("[bold]Pointers[/]");

        // assemble
        table.AddColumn(new TableColumn(legend.LeftAligned()));
        table.AddColumn(new TableColumn(local).Centered());
        table.AddColumn(new TableColumn(remote).Centered());

        AnsiConsole.Write(table);
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
