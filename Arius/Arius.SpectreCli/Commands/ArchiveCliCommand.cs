using System;   
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.CliSpectre.Utils;
using Arius.Core.Commands;
using Arius.Core.Commands.Archive;
using Arius.Core.Extensions;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using AriusCoreCommand = Arius.Core.Commands; //there is a conflict between Spectre.Console.Cli.ICommand and Arius.Core.Commands.ICommand

namespace Arius.CliSpectre.Commands;

internal class ArchiveCliCommand : AsyncCommand<ArchiveCliCommand.ArchiveCommandOptions>
{
    public ArchiveCliCommand(IAnsiConsole console, 
        ILogger<ArchiveCliCommand> logger, 
        AriusCoreCommand.ICommand<IArchiveCommandOptions> archiveCommand,
        ArchiveCommandStatistics statisticsProvider)
    {
        this.console = console;
        this.logger = logger;
        this.archiveCommand = archiveCommand;
        this.statisticsProvider = statisticsProvider;

        logger.LogDebug("{0} initialized", nameof(ArchiveCliCommand));
    }

    private readonly ILogger<ArchiveCliCommand> logger;
    private readonly AriusCoreCommand.ICommand<IArchiveCommandOptions> archiveCommand;
    private readonly ArchiveCommandStatistics statisticsProvider;
    private IAnsiConsole console;

    internal class ArchiveCommandOptions : RepositoryOptions, IArchiveCommandOptions
    {
        public ArchiveCommandOptions(ILogger<ArchiveCommandOptions> logger, StateVersion version, string accountName, string accountKey, string container, string passphrase, DirectoryInfo path)
            : base(logger, accountName, accountKey,container, passphrase, path)
        {
            VersionUtc = version.VersionUtc;
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
        public new DirectoryInfo Path => (DirectoryInfo)base.Path;

        public DateTime VersionUtc { get; }

        public override ValidationResult Validate()
        {
            var validTiers = new[] { AccessTier.Hot, AccessTier.Cool, AccessTier.Archive };
            if (!validTiers.Contains(Tier))
                return ValidationResult.Error($"'{Tier}' is not a valid tier");

            if (!Path.Exists)
                return ValidationResult.Error($"{Path} does not exist");

            return base.Validate();
        }
    }

    public override ValidationResult Validate(CommandContext context, ArchiveCommandOptions settings)
    {
        /*
         * For a reason unknown to me, the Validate on the ArchiveCommandOptions SHOULD be called as part of the override but they are not
         * Hence calling it manually
         * See https://github.com/spectreconsole/spectre.console/discussions/217 for a working example
         */

        var v = settings.Validate();

        if (!v.Successful)
            return v;

        return base.Validate(context, settings);
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ArchiveCommandOptions options)
    {
        logger.LogInformation($"Starting {nameof(ArchiveCliCommand)} from '{options.Path}' to '{options.AccountName}/{options.Container}'");

        foreach (var property in options.GetType().GetProperties())
        {
            var value = property.GetValue(options)?.ToString();
            if (Attribute.IsDefined(property, typeof(ObfuscateInLogAttribute)))
                value = "***";

            logger.LogDebug($"{property.Name}: {value}");
        }

        await archiveCommand.ExecuteAsync(options);


        var rule = new Rule("[red]Summary[/]");
        AnsiConsole.Write(rule);


        // Create summary table
        var s = (ArchiveCommandStatistics)statisticsProvider;

        var table = new Table();
        table.AddColumn("");
        table.AddColumn("");
        table.AddColumn(new TableColumn("Before").Centered());
        table.AddColumn(new TableColumn("Archive Operation").Centered());
        table.AddColumn(new TableColumn("After").Centered());

        table.AddRow("Local files", "(1) Files",   $"{s.localBeforeFiles}", $"{s.localDeltaFiles:+#;-#;0}", $"{s.localBeforeFiles + s.localDeltaFiles}");
        table.AddRow("",            "(2) Size",    $"{s.localBeforeSize.GetBytesReadable()}", $"+{s.localDeltaSize.GetBytesReadable()}", $"{(s.localBeforeSize + s.localDeltaSize).GetBytesReadable()}");
        table.AddRow("",            "(3) Entries", $"{s.localBeforePointerFiles}", $"{s.localDeltaPointerFiles:+#;-#;0}", $"{s.localBeforePointerFiles + s.localDeltaPointerFiles}");

        table.AddEmptyRow();

        table.AddRow("Remote repository", "(4) Binaries", $"{s.remoteBeforeBinaries}", $"{s.remoteDeltaBinaries:+#;-#;0}", $"{s.remoteAfterBinaries}");
        table.AddRow("",                  "(5) Size", $"{s.remoteBeforeSize.GetBytesReadable()}", $"+{s.remoteDeltaSize.GetBytesReadable()}", $"{s.remoteAfterSize.GetBytesReadable()}");
        table.AddRow("",                  "(6) Entries", $"{s.remoteBeforePointerFileEntries}", $"{s.remoteDeltaPointerFileEntries:+#;-#;0}", $"{s.remoteAfterPointerFileEntries}");


        //table.AddRow("Baz", "[green]Qux[/]");
        //table.AddRow(new Markup("[blue]Corgi[/]"), new Panel("Waldo"));

        AnsiConsole.Write(table);

        console.WriteLine("(1) ...");
        console.WriteLine("(2) ...");
        console.WriteLine("(3) ...");
        console.WriteLine("(4) ...");
        console.WriteLine("(5) Compressed and encrypted size of unique binaries");
        console.WriteLine("(6) ...");

        console.WriteLine();

        var duration = DateTime.UtcNow - options.VersionUtc;
        console.WriteLine($"Duration: {duration:g}s");
        console.WriteLine($"Speed: {(double)s.localDeltaSize / 1024 / 1024 / duration.TotalSeconds} MBps");




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