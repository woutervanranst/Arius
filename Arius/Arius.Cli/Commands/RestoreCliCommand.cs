using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Arius.Cli.Utils;
using Arius.Core.Commands.Restore;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Arius.Cli.Commands;

internal class RestoreCliCommand : AsyncCommand<RestoreCliCommand.RestoreCommandOptions>
{
    public RestoreCliCommand(Arius.Core.Commands.ICommand<IRestoreCommandOptions> restoreCommand)
    {
        this.restoreCommand = restoreCommand;
    }

    private readonly Arius.Core.Commands.ICommand<IRestoreCommandOptions> restoreCommand;

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
        [DefaultValue(true)] //TODO there is no way of turning this off
        public bool KeepPointers { get; init; }

        //[Description("")]
        //[CommandOption("-t|--pointintime <POINT_IN_TIME>")]
        public DateTime? PointInTimeUtc { get; init; }

        [Description("Local path")]
        [TypeConverter(typeof(StringToDirectoryInfoTypeConverter))]
        [CommandArgument(0, "[PATH]")] //Path is really mandatory but is not passed explicitly when running in Docker - so the Spectre.Console.Cli.CommandValidator.ValidateRequiredParameters fails
        public DirectoryInfo? Path { get; set; } // set - not init because it needs to be (re)set in the Interceptor
    }

    public override ValidationResult Validate(CommandContext context, RestoreCommandOptions settings)
    {
        var r = restoreCommand.Validate(settings);
        if (!r.IsValid)
            return ValidationResult.Error(r.ToString());

        return ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, RestoreCommandOptions options)
    {
        //logger.LogInformation($"Starting {nameof(RestoreCliCommand)} from '{options.AccountName}/{options.Container}' to '{options.Path}'");

        //logger.LogProperties(options);

        return await restoreCommand.ExecuteAsync(options);
    }
}