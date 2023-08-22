using Arius.Cli.Utils;
using Arius.Core.Extensions;
using Arius.Core.Facade;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace Arius.Cli.Commands;

internal class RestoreCliCommand : AsyncCommand<RestoreCliCommand.RestoreCommandOptions>
{
    public RestoreCliCommand(ILogger<RestoreCliCommand> logger, Facade facade)
    {
        this.logger = logger;
        this.facade = facade;
    }

    private readonly ILogger<RestoreCliCommand> logger;
    private readonly Facade                  facade;

    internal class RestoreCommandOptions : RepositoryOptions
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
        [DefaultValue(true)] //TODO there is no way of turning this off, see https://github.com/spectreconsole/spectre.console/issues/193#issuecomment-973816640
        public bool KeepPointers { get; init; }

        //[Description("")]
        //[CommandOption("-t|--pointintime <POINT_IN_TIME>")]
        public DateTime? PointInTimeUtc { get; init; }

        [Description("Local path")]
        [TypeConverter(typeof(StringToDirectoryInfoTypeConverter))]
        [CommandArgument(0, "[PATH]")] //Path is really mandatory but is not passed explicitly when running in Docker - so the Spectre.Console.Cli.CommandValidator.ValidateRequiredParameters fails
        public DirectoryInfo? Path { get; set; } // set - not init because it needs to be (re)set in the Interceptor
    }

    public override ValidationResult Validate(CommandContext context, RestoreCommandOptions options)
    {
        var r = RepositoryFacade.ValidateRestoreCommandOptions(options.AccountName, options.AccountKey, options.ContainerName, options.Passphrase, options.Path, options.Synchronize, options.Download, options.KeepPointers, options.PointInTimeUtc);
        if (!r.IsValid)
            return ValidationResult.Error(r.ToString());

        return ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, RestoreCommandOptions options)
    {
        try
        {
            logger.LogInformation($"Starting {nameof(RestoreCliCommand)} from '{options.AccountName}/{options.ContainerName}' to '{options.Path}'");
            logger.LogProperties(options);

            using var rf = await facade
                .ForStorageAccount(options.AccountName, options.AccountKey)
                .ForRepositoryAsync(options.ContainerName, options.Passphrase);

            if (options.PointInTimeUtc.HasValue)
                return await rf.ExecuteRestoreCommandAsync(options.Path, options.Synchronize, options.Download, options.KeepPointers, options.PointInTimeUtc!.Value);
            else
                return await rf.ExecuteRestoreCommandAsync(options.Path, options.Synchronize, options.Download, options.KeepPointers);
        }
        catch (Exception e)
        {
            // Logging needs to happen here (not in the error handler of the Main method) or otherwise https://github.com/adams85/filelogger/issues/15#issuecomment-927122196
            logger.LogError(e);

            throw;
        }
    }
}