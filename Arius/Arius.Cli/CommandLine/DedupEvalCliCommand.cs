using Arius.Cli.Extensions;
using Arius.Core.Configuration;
using Arius.Core.Facade;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Cli.CommandLine;

internal class DedupEvalCliCommand : ICliCommand
{
    public Command GetCommand()
    {
        var dedupEvalCommand = new Command(
            name: "dedupeval", 
            description: "EVALUATE DEDUP");

        Argument pathArgument;
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            pathArgument = new Argument<string>(
                name: "path",
                getDefaultValue: () => "/archive",
                description: "Path to evaluate");
        }
        else
        {
            pathArgument = new Argument<string>(
                name: "path",
                description: "Path to evaluate");
        }
        dedupEvalCommand.AddArgument(pathArgument);


        dedupEvalCommand.Handler = CommandHandler
            .Create<string, IHost>(
                async (path, host) =>
                {
                    ILogger logger = default;

                    try
                    {
                        logger = host.Services.GetRequiredService<ILogger<DedupEvalCliCommand>>();

                        logger.LogInformation("Creating Facade...");
                        var facade = host.Services.GetRequiredService<IFacade>();

                        logger.LogInformation($@"Creating DedupEvalCommand: evaluating '{path}'...");
                        var c = facade.CreateDedupEvalCommand(path);

                        logger.LogInformation("Executing Command...");
                        var r = await c.Execute();
                        logger.LogInformation("Executing Command... Done");

                        return r;
                    }
                    catch (Exception e)
                    {
                        logger?.LogError(e, $"Error in {nameof(CommandHandler)} of {this.GetType().Name}");
                    }
                    finally
                    {
                        logger?.LogInformation("Deleting tempdir...");

                        var tempDir = host.Services.GetRequiredService<IOptions<TempDirectoryAppSettings>>().Value.TempDirectory;
                        if (tempDir.Exists) tempDir.Delete(true);

                        logger?.LogInformation("Deleting tempdir... done");
                    }

                    return (int)Program.ExitCode.ERROR;
                });

        return dedupEvalCommand;
    }
}