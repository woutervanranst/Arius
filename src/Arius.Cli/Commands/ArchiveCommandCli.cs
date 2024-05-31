using System.ComponentModel;
using Arius.Core.Application.Commands;
using MediatR;
using Spectre.Console.Cli;

namespace Arius.Cli.Commands;

public class ArchiveCommandCli : AsyncCommand<ArchiveCommandCli.Settings>
{
    private readonly IMediator _mediator;

    public ArchiveCommandCli(IMediator mediator)
    {
        _mediator = mediator;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FilePath>")]
        [Description("The path of the file to archive")]
        public string FilePath { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var command = new ArchiveCommand
        {
            FilePath = settings.FilePath
        };

        await _mediator.Send(command);
        return 0;
    }
}