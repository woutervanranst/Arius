using CliFx.Exceptions;
using CliFx.Infrastructure;
using Mediator;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Arius.Cli.CliCommands;

public abstract class ProgressCliCommand<TCommand, TResult, TProgressUpdate> : CliFx.ICommand
    where TCommand : ICommand<TResult>
{
    private readonly IMediator mediator;
    private readonly ILogger logger;

    protected ProgressCliCommand(IMediator mediator, ILogger logger)
    {
        this.mediator = mediator;
        this.logger = logger;
    }

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
                    // Create the Mediator command from the CLI arguments with inline progress handler
                    var command = CreateCommand(ctx);

                    // Send the command and execute
                    var cancellationToken = console.RegisterCancellationHandler();
                    await mediator.Send(command, cancellationToken);

                    AnsiConsole.MarkupLine("[green]All files processed![/]");
                });
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unhandled exception");
            throw new CommandException(e.Message, showHelp: false, innerException: e);
        }
    }

    protected abstract TCommand CreateCommand(ProgressContext ctx);
}
