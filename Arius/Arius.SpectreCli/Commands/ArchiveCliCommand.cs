using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Arius.SpectreCli.Commands;

public class ArchiveCliCommand : AsyncCommand<ArchiveCliCommand.ArchiveSettings>
{
    public ArchiveCliCommand(IAnsiConsole console, ILogger<ArchiveCliCommand> logger)
    {
        this.console = console;
        this.logger = logger;
        this.logger.LogDebug("{0} initialized", nameof(Commands.ArchiveCliCommand));
    }

    private ILogger<ArchiveCliCommand> logger;
    private IAnsiConsole console;

    public class ArchiveSettings : RepositorySettings
    {
        [Description("Storage tier to use (hot|cool|archive)")]
        [CommandOption("-t|--tier <TIER>")]
        [DefaultValue("archive")]
        public string Tier { get; set; }

        [Description("Remove local file after a successful upload")]
        [CommandOption("--remove-local")]
        [DefaultValue(false)]
        public bool RemoveLocal { get; set; }

        [Description("Deduplicate the chunks in the binary files")]
        [CommandOption("--dedup")]
        [DefaultValue(false)]
        public bool Dedup { get; set; }

        [Description("Use the cached hash of a file (faster, do not use in an archive where file contents change)")]
        [CommandOption("--fasthash")]
        [DefaultValue(false)]
        public bool Fasthash { get; set; }

        public override Spectre.Console.ValidationResult Validate()
        {
            if (Tier is null)
                return Spectre.Console.ValidationResult.Error($"Tier is required");

            string[] validTiers = { "hot", "cool", "archive" };
            Tier = Tier.ToLowerInvariant();
            if (!validTiers.Contains(Tier))
                return Spectre.Console.ValidationResult.Error($"'{Tier}' is not a valid tier");

            return base.Validate();
        }
    }

    //public override ValidationResult Validate(CommandContext context, ArchiveSettings settings)
    //{
    //    if (settings.Project is null)
    //        return ValidationResult.Error($"Path not found");

    //    return base.Validate(context, settings);
    //}

    public override async Task<int> ExecuteAsync(CommandContext context, ArchiveSettings settings)
    {
        logger.LogInformation("Starting my command");
        AnsiConsole.MarkupLine($"Hello, [blue]{settings.Path}[/]");
        logger.LogInformation("Completed my command");

        return 0;
    }
}