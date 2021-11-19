using System;   
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.CliSpectre.Utils;
using Arius.Core.Commands;
using Arius.Core.Commands.Archive;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using AriusCoreCommand = Arius.Core.Commands; //there is a conflict between Spectre.Console.Cli.ICommand and Arius.Core.Commands.ICommand

namespace Arius.CliSpectre.Commands;

internal class ArchiveCliCommand : AsyncCommand<ArchiveCliCommand.ArchiveCommandOptions>
{
    public ArchiveCliCommand(IAnsiConsole console, ILogger<ArchiveCliCommand> logger, AriusCoreCommand.ICommand<IArchiveCommandOptions> archiveCommand)
    {
        this.console = console;
        this.logger = logger;
        this.archiveCommand = archiveCommand;

        logger.LogDebug("{0} initialized", nameof(ArchiveCliCommand));
    }

    private ILogger<ArchiveCliCommand> logger;
    private readonly AriusCoreCommand.ICommand<IArchiveCommandOptions> archiveCommand;
    private IAnsiConsole console;

    internal class ArchiveCommandOptions : RepositoryOptions, IArchiveCommandOptions
    {
        public ArchiveCommandOptions(string accountName, string accountKey, string container, string passphrase, DirectoryInfo path)
            //AccessTier tier, bool removeLocal, bool dedup, bool fastHash, DirectoryInfo path)
            : base(accountName, accountKey,container, passphrase, path)
        {
            Path = path;
        }

        [Description("Storage tier to use (hot|cool|archive)")]
        [TypeConverter(typeof(StringToAccessTierTypeConverter))]
        [CommandOption("-t|--tier <TIER>")]
        [DefaultValue("archive")]
        public AccessTier Tier { get; init; }

        [Description("Remove local file after a successful upload")]
        [CommandOption("--remove-local")]
        [DefaultValue(false)]
        public bool RemoveLocal { get; init; }

        [Description("Deduplicate the chunks in the binary files")]
        [CommandOption("--dedup")]
        [DefaultValue(false)]
        public bool Dedup { get; init; }

        [Description("Use the cached hash of a file (faster, do not use in an archive where file contents change)")]
        [CommandOption("--fasthash")]
        [DefaultValue(false)]
        public bool FastHash { get; init; }

        [Description("Local path")]
        [TypeConverter(typeof(StringToDirectoryInfoTypeConverter))]
        [CommandArgument(0, "<PATH>")]
        public DirectoryInfo Path { get; }

        public DateTime VersionUtc => DateTime.UtcNow;

        public override ValidationResult Validate()
        {
            //if (PathInternal is not DirectoryInfo)
            //    return ValidationResult.Error($"Tier is required");

            //string[] validTiers = { "hot", "cool", "archive" };
            //Tier = Tier.ToLowerInvariant();
            //if (!validTiers.Contains(Tier))
            //    return ValidationResult.Error($"'{Tier}' is not a valid tier");

            return base.Validate();
        }

        //public override ValidationResult Validate()
        //{
        //    if (AccountName is null)
        //        return ValidationResult.Error($"AccountName is required");

        //    if (AccountKey is null)
        //        return ValidationResult.Error($"AccountKey is required");

        //    if (Container is null)
        //        return ValidationResult.Error($"Container is required");

        //    if (Passphrase is null)
        //        return ValidationResult.Error($"Passphrase is required");

        //    if (PathInternal is null)
        //        return ValidationResult.Error($"Path is required");


        //    // Save the Config
        //    PersistedRepositoryConfigReader.SaveSettings(this, (DirectoryInfo)PathInternal);

        //    return base.Validate();
        //}
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

        await archiveCommand.ExecuteAsync(options);
        
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