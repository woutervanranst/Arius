using Spectre.Console.Cli;

namespace Arius.Cli.Utils;

internal class CommandInterceptor : ICommandInterceptor
{
    private readonly ParsedOptionsProvider op;

    public CommandInterceptor(ParsedOptionsProvider op)
    {
        this.op = op;
    }
    //public static readonly LoggingLevelSwitch LogLevel = new();

    public void Intercept(CommandContext context, CommandSettings options)
    {
        // this is run after parsing the arguments, but before the commands

        op.Options = options;


        //if (settings is LogCommandSettings logSettings)
        //{
        //    LoggingEnricher.Path = logSettings.LogFile ?? "application.log";
        //    //LogLevel.MinimumLevel = logSettings.LogLevel;
        //}
    }
}