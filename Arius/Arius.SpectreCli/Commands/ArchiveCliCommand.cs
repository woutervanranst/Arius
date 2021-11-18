using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Commands;
using Arius.Core.Commands.Archive;
using Arius.SpectreCli.Utils;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Arius.CliSpectre.Commands;

internal class ArchiveCliCommand : AsyncCommand<ArchiveCliCommand.ArchiveCommandOptions>
{
    public ArchiveCliCommand(IAnsiConsole console, ILogger<ArchiveCliCommand> logger, Core.Commands.ICommand<IArchiveCommandOptions> command)
    {
        this.console = console;
        this.logger = logger;
        this.command = command;
        this.logger.LogDebug("{0} initialized", nameof(ArchiveCliCommand));
    }

    private ILogger<ArchiveCliCommand> logger;
    private readonly Core.Commands.ICommand<IArchiveCommandOptions> command;
    private IAnsiConsole console;

    internal class ArchiveCommandOptions : RepositoryOptions, IArchiveCommandOptions
    {
        [Description("Storage tier to use (hot|cool|archive)")]
        [TypeConverter(typeof(StringToAccessTierTypeConverter))]
        [CommandOption("-t|--tier <TIER>")]
        [DefaultValue("archive")]
        public AccessTier Tier { get; set; }


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
        public bool FastHash { get; set; }

        public DateTime VersionUtc => DateTime.UtcNow;

        public override ValidationResult Validate()
        {
            //if (Tier is null)
            //    return ValidationResult.Error($"Tier is required");

            //string[] validTiers = { "hot", "cool", "archive" };
            //Tier = Tier.ToLowerInvariant();
            //if (!validTiers.Contains(Tier))
            //    return ValidationResult.Error($"'{Tier}' is not a valid tier");

            return base.Validate();
        }
    }

    //public override ValidationResult Validate(CommandContext context, ArchiveSettings settings)
    //{
    //    if (settings.Project is null)
    //        return ValidationResult.Error($"Path not found");

    //    return base.Validate(context, settings);
    //}

    public override async Task<int> ExecuteAsync(CommandContext context, ArchiveCommandOptions options)
    {
        logger.LogInformation("Starting my command");

        await command.ExecuteAsync(options);
        
        //AnsiConsole.MarkupLine($"Hello, [blue]{settings.Path}[/]");
        //logger.LogInformation("Completed my command");

        //    var table = new Table().RoundedBorder();
        //    table.AddColumn("[grey]Name[/]");
        //    table.AddColumn("[grey]Value[/]");

        //    var properties = settings.GetType().GetProperties();
        //    foreach (var property in properties)
        //    {
        //        var value = property.GetValue(settings)
        //            ?.ToString()
        //            ?.Replace("[", "[[");

        //        table.AddRow(
        //            property.Name,
        //            value ?? "[grey]null[/]");
        //    }

        //    AnsiConsole.Write(table);

        return 0;
    }
}