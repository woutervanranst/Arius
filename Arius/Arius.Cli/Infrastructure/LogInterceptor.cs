using Spectre.Console.Cli;

namespace Arius.Cli.Infrastructure;

public class LogInterceptor : ICommandInterceptor
{
    //public static readonly LoggingLevelSwitch LogLevel = new();

    public void Intercept(CommandContext context, CommandSettings settings)
    {
        // this is run after parsing the arguments, but before the commands

        //if (settings is LogCommandSettings logSettings)
        //{
        //    LoggingEnricher.Path = logSettings.LogFile ?? "application.log";
        //    //LogLevel.MinimumLevel = logSettings.LogLevel;
        //}
    }
}