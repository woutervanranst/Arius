using System.Threading.Tasks;
using Arius.Core.Commands.Rehydrate;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Arius.Cli.Commands;

/* When adding a new Command:
 *  Arius.Core
 *      in Facade.AddAriusCoreCommands:         .AddSingleton<ICommand<IRehydrateCommandOptions>, RehydrateCommand>();
 *      options:                        public interface IRehydrateCommandOptions : IRepositoryOptions
 *      a new Command:                  internal class RehydrateCommand : ICommand<IRehydrateCommandOptions>
 *
 *  Arius.Cli
 *      a new CliCommand:               RehydrateCliCommand : AsyncCommand<RehydrateCliCommand.RehydrateCommandOptions>
 *          with concrete impl of the new OptionsInterface
 *      config.AddCommand<RehydrateCliCommand>("rehydrate");
 *      RehydrateCliCommand.RehydrateCommandOptions => "rehydrate",
 *
 */

internal class RehydrateCliCommand : AsyncCommand<RehydrateCliCommand.RehydrateCommandOptions>
{
    public RehydrateCliCommand(ILogger<RehydrateCliCommand> logger,
        Arius.Core.Commands.ICommand<IRehydrateCommandOptions> rehydrateCommand)
    {
        this.rehydrateCommand = rehydrateCommand;

        logger.LogDebug("{0} initialized", nameof(RestoreCliCommand));
    }

    private readonly Arius.Core.Commands.ICommand<IRehydrateCommandOptions> rehydrateCommand;

    internal class RehydrateCommandOptions : RepositoryOptions, IRehydrateCommandOptions
    {
        // No special requirements
    }

    public override ValidationResult Validate(CommandContext context, RehydrateCommandOptions settings)
    {
        var r = rehydrateCommand.Validate(settings);
        if (!r.IsValid)
            return ValidationResult.Error(r.ToString());

        return ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, RehydrateCommandOptions options)
    {
        return await rehydrateCommand.ExecuteAsync(options);
    }
}