using Arius.Core.Commands.RestoreCommand;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using Mediator;
using Microsoft.Extensions.Logging;
using System;
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
                //ProgressReporter = pu
            };

            var cancellationToken = console.RegisterCancellationHandler();
            await mediator.Send(command, cancellationToken);
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