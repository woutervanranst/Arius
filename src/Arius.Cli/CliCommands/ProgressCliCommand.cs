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
                    var progressUpdates = Channel.CreateUnbounded<TProgressUpdate>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false });

                    // Create the Mediator command from the CLI arguments
                    var command = CreateCommand(new Progress<TProgressUpdate>(u => progressUpdates.Writer.TryWrite(u)));

                    // Send the command and start the progress display loop
                    var cancellationToken = console.RegisterCancellationHandler();
                    var commandTask = mediator.Send(command, cancellationToken).AsTask();
                    commandTask.ContinueWith(_ => progressUpdates.Writer.Complete());

                    var taskDictionary = new ConcurrentDictionary<string, ProgressTask>();

                    // Process progress updates as they arrive
                    await foreach (var u in progressUpdates.Reader.ReadAllAsync(cancellationToken))
                    {
                        HandleProgressUpdate(u, ctx, taskDictionary);
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

    protected abstract TCommand CreateCommand(IProgress<TProgressUpdate> progressReporter);

    protected abstract void HandleProgressUpdate(TProgressUpdate update, ProgressContext ctx, ConcurrentDictionary<string, ProgressTask> taskDictionary);
}
