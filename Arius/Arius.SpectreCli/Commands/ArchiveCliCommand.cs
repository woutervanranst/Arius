using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Spectre.Console.Examples
{
    public class ArchiveCliCommand : Command<ArchiveCliCommand.Settings>
    {
        private ILogger<ArchiveCliCommand> _logger;
        private IAnsiConsole _console;

        public ArchiveCliCommand(IAnsiConsole console, ILogger<ArchiveCliCommand> logger)
        {
            _console = console;
            _logger = logger;
            _logger.LogDebug("{0} initialized", nameof(ArchiveCliCommand));
        }

        public class Settings : CommandSettings
        {
            [CommandArgument(0, "[Name]")]
            public string Name { get; set; }
        }


        public override int Execute(CommandContext context, Settings settings)
        {
            _logger.LogInformation("Starting my command");
            AnsiConsole.MarkupLine($"Hello, [blue]{settings.Name}[/]");
            _logger.LogInformation("Completed my command");

            return 0;
        }
    }
}