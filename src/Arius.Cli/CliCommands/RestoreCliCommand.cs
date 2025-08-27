using Arius.Core.Commands;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using Mediator;
using Serilog;
using System;
using System.Threading.Tasks;

namespace Arius.Cli.CliCommands;

public abstract class RestoreCliCommandBase : CliFx.ICommand
{
    private readonly IMediator mediator;

    public RestoreCliCommandBase(IMediator mediator)
    {
        this.mediator = mediator;
    }

    public abstract string[] Targets { get; init; }

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
        try
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
                Targets       = Targets,
                //ProgressReporter = pu
            };

            var cancellationToken = console.RegisterCancellationHandler();
            await mediator.Send(command, cancellationToken);
        }
        catch (Exception e)
        {
            Log.Fatal(e, "Unhandled exception");
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

[Command("restore", Description = "Restores a directory from Azure Blob Storage.")]
public class RestoreCliCommand : RestoreCliCommandBase
{
    public RestoreCliCommand(IMediator mediator) : base(mediator)
    {
    }

    [CommandParameter(0, Description = "Directory or files to restore.")]
    public override required string[] Targets { get; init; }
}



[Command("restore", Description = "Restores a directory from Azure Blob Storage. [Docker]")]
public class RestoreDockerCliCommand : RestoreCliCommandBase
{
    public RestoreDockerCliCommand(IMediator mediator) : base(mediator)
    {
    }

    public override required string[] Targets
    {
        get => ["/archive"];
        init => throw new InvalidOperationException("Targets cannot be set in Docker");
    }
}