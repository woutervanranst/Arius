using Arius.Cli.Commands;
using Arius.Core.Commands;
using Spectre.Console.Cli;
using System;
using System.IO;
using static Arius.Cli.Commands.ArchiveCliCommand;

namespace Arius.Cli.Utils;

internal class CommandInterceptor : ICommandInterceptor
{
    //public static readonly LoggingLevelSwitch LogLevel = new();

    private CommandContext context;
    private CommandSettings options;

    public void Intercept(CommandContext context, CommandSettings options)
    {
        // this is run after parsing the arguments, after the validation inside the CommandSetting but before Command.Validate and Command.Execute

        if (options is RepositoryOptions o1)
        {
            if (string.IsNullOrEmpty(o1.AccountName))
            {
                // AccountName was not set in the command line, so we try to get it from the environment variable
                o1.AccountName = Environment.GetEnvironmentVariable(Program.AriusAccountNameEnvironmentVariableName); //TODO check https://github.com/spectreconsole/spectre.console/issues/539
            }

            if (string.IsNullOrEmpty(o1.AccountKey))
            {
                // AccountKey was not set in the command line, so we try to get it from the environment variable
                o1.AccountKey = Environment.GetEnvironmentVariable(Program.AriusAccountKeyEnvironmentVariableName);
            }
        }

        if (options is ArchiveCommandOptions o2)
        {
            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            {
                if (o2.Path is not null)
                    throw new InvalidOperationException("DOTNET_RUNNING_IN_CONTAINER is true but PATH argument is specified");

                o2.Path = new DirectoryInfo("/archive"); //when runnning in a docker container
            }
        }
        else
        {
            throw new NotImplementedException();
        }

        //    // 2. Try to load from config file
        //    // TODO Test precedence: Environment Variable < Settings < Cli
        //    var c = PersistedRepositoryConfigReader.LoadSettings(path, passphrase);
        //    if (c != default)
        //    {
        //        AccountName ??= c.accountName; // if the CLI option is not specified, AccountName will be null
        //        AccountKey ??= c.accountKey;
        //        Container ??= c.container;

        //        logger.LogDebug("Loaded options from configfile");
        //    }
        //    else
        //        logger.LogDebug("Could not load options from file");

        //    //// Save the Config
        //    //if (Path is DirectoryInfo di)
        //    //{
        //    //    logger.LogDebug("Saving options");
        //    //    PersistedRepositoryConfigReader.SaveSettings(logger, this, di);
        //    //}
        //    //else
        //    //{
        //    //    logger.LogDebug("Path is not a directory, not saving options");
        //    //}

        this.context = context;
        this.options = options;

        Program.Instance.ParsedOptions = (ICommandOptions)options;

        //if (settings is LogCommandSettings logSettings)
        //{
        //    LoggingEnricher.Path = logSettings.LogFile ?? "application.log";
        //    //LogLevel.MinimumLevel = logSettings.LogLevel;
        //}
    }

    public string CommandName => context.Name;
}