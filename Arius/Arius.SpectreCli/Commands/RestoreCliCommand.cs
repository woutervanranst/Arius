using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Arius.CliSpectre.Utils;
using Arius.Core.Commands;
using Arius.Core.Commands.Restore;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using AriusCoreCommand = Arius.Core.Commands; //there is a conflict between Spectre.Console.Cli.ICommand and Arius.Core.Commands.ICommand

namespace Arius.CliSpectre.Commands;

internal class RestoreCliCommand : AsyncCommand<RestoreCliCommand.RestoreCommandOptions>
{
    public RestoreCliCommand(IAnsiConsole console, ILogger<RestoreCliCommand> logger, AriusCoreCommand.ICommand<IRestoreCommandOptions> restoreCommand)
    {
        this.console = console;
        this.logger = logger;
        this.restoreCommand = restoreCommand;

        logger.LogDebug("{0} initialized", nameof(RestoreCliCommand));
    }

    private readonly IAnsiConsole console;
    private readonly ILogger<RestoreCliCommand> logger;
    private readonly AriusCoreCommand.ICommand<IRestoreCommandOptions> restoreCommand;


    internal class RestoreCommandOptions : RepositoryOptions, IRestoreCommandOptions
    {
        public RestoreCommandOptions(ILogger<RestoreCommandOptions> logger, string accountName, string accountKey, string container, string passphrase, DirectoryInfo path)
            : base(logger, accountName, accountKey, container, passphrase, path)
        {
        }

        [Description("Create pointers on local for every remote file, without actually downloading the files")]
        [CommandOption("-s|--synchronize")]
        [DefaultValue(false)]
        public bool Synchronize { get; init; }

        [Description("Download files for the given pointer in <PATH> (file) or all the pointers in <PATH> (folder)")]
        [CommandOption("--download")]
        [DefaultValue(false)]
        public bool Download { get; init; }

        [Description("Keep pointer files after downloading content files")]
        [CommandOption("--keep-pointers")]
        [DefaultValue(true)]
        public bool KeepPointers { get; init; }

        //[Description("")]
        //[CommandOption("-t|--pointintime <POINT_IN_TIME>")]
        public DateTime? PointInTimeUtc { get; init; }

        [Description("Local path")]
        [TypeConverter(typeof(StringToDirectoryInfoTypeConverter))]
        [CommandArgument(0, "<PATH>")]
        public new DirectoryInfo Path => (DirectoryInfo)base.Path;

        public override ValidationResult Validate()
        {
            if (!Path.Exists)
                return ValidationResult.Error($"{Path} does not exist");

            return base.Validate();
        }
    }

    public override ValidationResult Validate(CommandContext context, RestoreCommandOptions settings)
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

    public override async Task<int> ExecuteAsync(CommandContext context, RestoreCommandOptions options)
    {
        return await restoreCommand.ExecuteAsync(options);
    }
}