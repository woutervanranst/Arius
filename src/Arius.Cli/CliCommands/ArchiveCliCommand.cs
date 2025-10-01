using Arius.Core.Features.Commands.Archive;
using Arius.Core.Shared.Storage;
using CliFx.Attributes;
using Mediator;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Collections.Concurrent;

namespace Arius.Cli.CliCommands;

public abstract class ArchiveCliCommandBase : ProgressCliCommand<ArchiveCommand, Unit, ProgressUpdate>
{
    protected ArchiveCliCommandBase(IMediator mediator, ILogger<ArchiveCliCommandBase> logger) : base(mediator, logger)
    {
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

    protected override ArchiveCommand CreateCommand(IProgress<ProgressUpdate> progressReporter)
    {
        return new ArchiveCommand
        {
            AccountName      = AccountName,
            AccountKey       = AccountKey,
            ContainerName    = ContainerName,
            Passphrase       = Passphrase,
            RemoveLocal      = RemoveLocal,
            Tier             = Tier,
            LocalRoot        = LocalRoot,
            ProgressReporter = progressReporter
        };
    }

    protected override void HandleProgressUpdate(ProgressUpdate update, ProgressContext ctx, ConcurrentDictionary<string, ProgressTask> taskDictionary)
    {
        if (update is TaskProgressUpdate tpu)
        {
            var isError = tpu.Percentage < 0;
            var color = isError ? "red" : "blue";
            var task = taskDictionary.GetOrAdd(tpu.TaskName, taskName => ctx.AddTask($"[{color}]{taskName}[/]").IsIndeterminate());
            if (!string.IsNullOrWhiteSpace(tpu.StatusMessage))
                task.Description = $"[{color}]{tpu.TaskName}[/] ({tpu.StatusMessage})";
            task.Value = isError ? 0 : tpu.Percentage;
            if (tpu.Percentage >= 100)
                task.StopTask();
        }
        else if (update is FileProgressUpdate fpu)
        {
            var isError = fpu.Percentage < 0;
            var color = isError ? "red" : "blue";
            var task = taskDictionary.GetOrAdd(fpu.FileName, fileName => ctx.AddTask($"[{color}]{fileName}[/]"));
            task.Description = $"[{color}]{fpu.FileName.EscapeMarkup().TruncateAndRightJustify(50)}[/] ({fpu.StatusMessage?.TruncateAndLeftJustify(20)})";
            task.Value = isError ? 0 : fpu.Percentage;
            if (fpu.Percentage >= 100)
                task.StopTask();
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]Unknown progress update type: {update.GetType().Name}[/]");
        }
    }
}

[Command("archive", Description = "Archives a local directory to Azure Blob Storage.")]
public class ArchiveCliCommand: ArchiveCliCommandBase
{
    public ArchiveCliCommand(IMediator mediator, ILogger<ArchiveCliCommandBase> logger) : base(mediator, logger)
    {
    }

    [CommandParameter(0, Description = "Path to the local root directory to archive.")]
    public override required DirectoryInfo LocalRoot { get; init; }
}



[Command("archive", Description = "Archives a local directory to Azure Blob Storage. [Docker]")]
public class ArchiveDockerCliCommand : ArchiveCliCommandBase
{
    public ArchiveDockerCliCommand(IMediator mediator, ILogger<ArchiveCliCommandBase> logger) : base(mediator, logger)
    {
    }

    public override required DirectoryInfo LocalRoot
    {
        get => new("/archive");
        init => throw new InvalidOperationException("LocalRoot cannot be set in Docker");
    }
}
