using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Arius.Cli.Utils;
using Arius.Core.Commands.Archive;
using Arius.Core.Extensions;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Arius.Cli.Commands;

internal class ArchiveCliCommand : AsyncCommand<ArchiveCliCommand.ArchiveCommandOptions>
{
    public ArchiveCliCommand(ILogger<ArchiveCliCommand> logger, 
        Arius.Core.Commands.ICommand<IArchiveCommandOptions> archiveCommand)
    {
        this.logger = logger;
        this.archiveCommand = archiveCommand;
    }

    private readonly ILogger<ArchiveCliCommand> logger;
    private Core.Commands.ICommand<IArchiveCommandOptions> archiveCommand;

    internal class ArchiveCommandOptions : RepositoryOptions, IArchiveCommandOptions
    {
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
        [CommandArgument(0, "[PATH]")] //Path is really mandatory but is not passed explicitly when running in Docker - so the Spectre.Console.Cli.CommandValidator.ValidateRequiredParameters fails
        public DirectoryInfo? Path { get; set; } // set - not init because it needs to be (re)set in the Interceptor

        public DateTime VersionUtc { get; set; } // set - not init because it needs to be (re)set in the Interceptor
    }

    public override ValidationResult Validate(CommandContext context, ArchiveCommandOptions settings)
    {
        var r = archiveCommand.Validate(settings);
        if (!r.IsValid)
            return ValidationResult.Error(r.ToString());
        
        return ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ArchiveCommandOptions options)
    {
        try
        {
            logger.LogInformation($"Starting {nameof(ArchiveCliCommand)} from '{options.Path}' to '{options.AccountName}/{options.Container}'");

            logger.LogProperties(options);

            await archiveCommand.ExecuteAsync(options);
        }
        catch (Exception e)
        {
            // Logging needs to happen here or otherwise https://github.com/adams85/filelogger/issues/15#issuecomment-927122196
            logger.LogError(e);

            throw;
        }
        

        //console.WriteLine();
        //console.Write(new Rule("[red]Summary[/]"));

        //// Create summary table
        //var s = (ArchiveCommandStatistics)statisticsProvider;

        //var table = new Table()
        //    .AddColumn("")
        //    .AddColumn("")
        //    .AddColumn(new TableColumn("Before").Centered())
        //    .AddColumn(new TableColumn("Archive Operation").Centered())
        //    .AddColumn(new TableColumn("After").Centered());

        //table.AddRow("Local files", "(1) Files",   $"{s.localBeforeFiles}", $"{s.localDeltaFiles:+#;-#;0}", $"{s.localBeforeFiles + s.localDeltaFiles}");
        //table.AddRow("",            "(2) Size",    $"{s.localBeforeSize.GetBytesReadable()}", $"{PlusSignOnly(s.localDeltaSize)}{s.localDeltaSize.GetBytesReadable()}", $"{(s.localBeforeSize + s.localDeltaSize).GetBytesReadable()}");
        //table.AddRow("",            "(3) Entries", $"{s.localBeforePointerFiles}", $"{s.localDeltaPointerFiles:+#;-#;0}", $"{s.localBeforePointerFiles + s.localDeltaPointerFiles}");

        //table.AddEmptyRow();

        //table.AddRow("Remote repository", "(4) Binaries", $"{s.remoteBeforeBinaries}", $"{s.remoteDeltaBinaries:+#;-#;0}", $"{s.remoteAfterBinaries}");
        //table.AddRow("",                  "(5) Size", $"{s.remoteBeforeSize.GetBytesReadable()}", $"{PlusSignOnly(s.remoteDeltaSize)}{s.remoteDeltaSize.GetBytesReadable()}", $"{s.remoteAfterSize.GetBytesReadable()}");
        //table.AddRow("",                  "(6) Entries", $"{s.remoteBeforePointerFileEntries}", $"{s.remoteDeltaPointerFileEntries:+#;-#;0}", $"{s.remoteAfterPointerFileEntries}");

        ////table.AddRow("Baz", "[green]Qux[/]");
        ////table.AddRow(new Markup("[blue]Corgi[/]"), new Panel("Waldo"));

        //console.Write(table);

        //var r = new Recorder(console);
        //r.Write(table);
        //logger.LogDebug(r.ExportText()); //h4x0r LogInformation actually prints it to the screen


        //console.WriteLine("  (1) Number of files in the local path");
        //console.WriteLine("  (2) Size of the files in the local path");
        //console.WriteLine("  (3) Number of files + pointers in the local path (ie including 'thin' files)");
        //console.WriteLine("  (4) Number of unique binaries (in all versions)");
        //console.WriteLine("  (5) Compressed and encrypted size of unique binaries (in all versions)");
        //console.WriteLine("  (6) Number of files + pointers (in all versions)");

        //console.WriteLine();

        //console.WriteLine($"Number of versions: {s.versionCount}");
        //console.WriteLine($"Last version with changes: {s.lastVersion.ToLocalTime()} {(s.lastVersion == options.VersionUtc ? "(this run)" : "(no changes this run)")}");

        //console.WriteLine();

        //var duration = DateTime.UtcNow - options.VersionUtc;
        //console.WriteLine($"Duration: {duration:hh\\:mm\\:ss}s");
        //console.WriteLine($"Speed: {Math.Round((double)s.localDeltaSize / 1024 / 1024 / duration.TotalSeconds, 2)} MBps");
        

        return 0;
    }

    private static string PlusSignOnly(long number) =>
        number switch
        {
            > 0 => "+",
            _ => ""
        };
}