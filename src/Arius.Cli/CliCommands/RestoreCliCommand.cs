using Arius.Core.Features.Commands.Restore;
using CliFx.Attributes;
using Mediator;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Collections.Concurrent;

namespace Arius.Cli.CliCommands;

public abstract class RestoreCliCommandBase : ProgressCliCommand<RestoreCommand, RestoreCommandResult, ProgressUpdate>
{
    protected RestoreCliCommandBase(IMediator mediator, ILogger<RestoreCliCommandBase> logger) : base(mediator, logger)
    {
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

    protected override RestoreCommand CreateCommand(IProgress<ProgressUpdate> progressReporter)
    {
        return new RestoreCommand
        {
            AccountName      = AccountName,
            AccountKey       = AccountKey,
            ContainerName    = ContainerName,
            Passphrase       = Passphrase,
            LocalRoot        = LocalRoot,
            Targets          = Targets,
            Download         = Download,
            IncludePointers  = IncludePointers,
            ProgressReporter = progressReporter
        };
    }

    protected override void HandleProgressUpdate(ProgressUpdate update, ProgressContext ctx, ConcurrentDictionary<string, ProgressTask> taskDictionary)
    {
        if (update is TaskProgressUpdate tpu)
        {
            var isError = tpu.Percentage < 0;
            var color = isError ? "red" : "cyan1";
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
            var color = isError ? "red" : "cyan3";
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

[Command("restore", Description = "Restores a directory from Azure Blob Storage.")]
public class RestoreCliCommand : RestoreCliCommandBase
{
    public RestoreCliCommand(IMediator mediator, ILogger<RestoreCliCommandBase> logger) : base(mediator, logger)
    {
    }

    [CommandOption("root", 'r', Description = "Root directory for restore operation.")]
    public override DirectoryInfo LocalRoot { get; init; } = new(Environment.CurrentDirectory);
}



[Command("restore", Description = "Restores a directory from Azure Blob Storage. [Docker]")]
public class RestoreDockerCliCommand : RestoreCliCommandBase
{
    public RestoreDockerCliCommand(IMediator mediator, ILogger<RestoreCliCommandBase> logger) : base(mediator, logger)
    {
    }

    public override required DirectoryInfo LocalRoot
    {
        get => new("/archive");
        init => throw new InvalidOperationException("LocalRoot cannot be set in Docker");
    }
}