using Arius.Cli.Commands;
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

        if (options is RepositoryOptions o)
        {
            if (string.IsNullOrEmpty(o.AccountName))
            {
                // AccountName was not set in the command line, so we try to get it from the environment variable
                o.AccountName = Environment.GetEnvironmentVariable(Program.AriusAccountNameEnvironmentVariableName); //TODO check https://github.com/spectreconsole/spectre.console/issues/539
            }

            if (string.IsNullOrEmpty(o.AccountKey))
            {
                // AccountKey was not set in the command line, so we try to get it from the environment variable
                o.AccountKey = Environment.GetEnvironmentVariable(Program.AriusAccountKeyEnvironmentVariableName);
            }
        }
        
        

        //if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        //{
        //    if (o.Path is not null)
        //        throw new InvalidOperationException("DOTNET_RUNNING_IN_CONTAINER is true but PATH argument is specified");

        //    o.Path = new DirectoryInfo("/archive"); //when runnning in a docker container
        //}

        this.context = context;
        this.options = options;

        Program.Instance.ParsedOptions = options;

        //if (settings is LogCommandSettings logSettings)
        //{
        //    LoggingEnricher.Path = logSettings.LogFile ?? "application.log";
        //    //LogLevel.MinimumLevel = logSettings.LogLevel;
        //}
    }

    public string CommandName => context.Name;
}