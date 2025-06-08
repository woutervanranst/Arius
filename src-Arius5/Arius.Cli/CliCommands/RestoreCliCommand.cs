using Arius.Core.Commands;
using Arius.Core.Models;
using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using MediatR;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Arius.Cli.CliCommands;

[Command("restore", Description = "Restores a directory from Azure Blob Storage.")]
public sealed class RestoreCliCommand : ICommand
{
    private readonly IMediator _mediator;

    public RestoreCliCommand(IMediator mediator)
    {
        _mediator = mediator;
    }

    [CommandParameter(0, Description = "Path to the local root directory to restore to.")]
    public required DirectoryInfo LocalRoot { get; init; }

    [CommandOption("accountname", 'n', IsRequired = true, Description = "Azure Storage Account name.", EnvironmentVariable = "ARIUS_ACCOUNT_NAME")]
    public required string AccountName { get; init; }

    [CommandOption("accountkey", 'k', IsRequired = true, Description = "Azure Storage Account key.", EnvironmentVariable = "ARIUS_ACCOUNT_KEY")]
    public required string AccountKey { get; init; }

    [CommandOption("container", 'c', IsRequired = true, Description = "Azure Blob Storage container name.")]
    public required string ContainerName { get; init; }

    [CommandOption("passphrase", 'p', IsRequired = true, Description = "Passphrase for decryption.")]
    public required string Passphrase { get; init; }

    [CommandOption("synchronize", Description = "Synchronize the local directory with the remote state.")]
    public bool Synchronize { get; init; } = false;

    [CommandOption("download", Description = "Download the files.")]
    public bool Download { get; init; } = false;

    [CommandOption("keep-pointers", Description = "Keep the pointer files after restore.")]
    public bool KeepPointers { get; init; } = false;

    public async ValueTask ExecuteAsync(IConsole console)
    {
        var command = new RestoreCommand
        {
            AccountName   = AccountName,
            AccountKey    = AccountKey,
            ContainerName = ContainerName,
            Passphrase    = Passphrase,
            Synchronize   = Synchronize,
            Download      = Download,
            KeepPointers  = KeepPointers,
            LocalRoot     = LocalRoot,
            //ProgressReporter = pu
        };

        // Send the command and start the progress display loop
        var commandTask = _mediator.Send(command);

    }
}
