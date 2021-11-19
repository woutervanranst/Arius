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
        [TypeConverter(typeof(StringToFileSystemInfoTypeConverter))]
        [CommandArgument(0, "<PATH>")]
        public DirectoryInfo Path
        {
            get => (DirectoryInfo)PathInternal;
            init => PathInternal = value;
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, RestoreCommandOptions options)
    {
        //Console.WriteLine(options.Path);
        //throw new NotImplementedException();

        //return await restoreCommand.ExecuteAsync(options);

        return 0;
    }
}