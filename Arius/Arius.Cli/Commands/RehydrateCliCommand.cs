using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Arius.Core.Commands.Rehydrate;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using AriusCoreCommand = Arius.Core.Commands; //there is a conflict between Spectre.Console.Cli.ICommand and Arius.Core.Commands.ICommand

namespace Arius.Cli.Commands;

/* When adding a new Command:
 *  Arius.Core
 *      in Facade.AddAriusCore:         .AddSingleton<ICommand<IRehydrateCommandOptions>, RehydrateCommand>();
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
    public RehydrateCliCommand(IAnsiConsole console,
        ILogger<RehydrateCliCommand> logger,
        AriusCoreCommand.ICommand<IRehydrateCommandOptions> rehydrateCommand)
    {
        this.rehydrateCommand = rehydrateCommand;

        logger.LogDebug("{0} initialized", nameof(RestoreCliCommand));
    }

    private readonly AriusCoreCommand.ICommand<IRehydrateCommandOptions> rehydrateCommand;

    internal class RehydrateCommandOptions : RepositoryOptions, IRehydrateCommandOptions
    {
        public RehydrateCommandOptions(ILogger<RepositoryOptions> logger, string accountName, string accountKey, string container, string passphrase) : base(logger, accountName, accountKey, container, passphrase, null)
        {
        }
    }

    

    public override async Task<int> ExecuteAsync(CommandContext context, RehydrateCommandOptions options)
    {
        return await rehydrateCommand.ExecuteAsync(options);
    }
}