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

internal class ArchiveCliCommand : ICliCommand
{
    public ArchiveCliCommand(DateTime versionUtc)
    {
        this.versionUtc = versionUtc;
    }

    private readonly DateTime versionUtc;

    public Command GetCommand()
    {
        var archiveCommand = new Command(
            name: "archive", 
            description: "Archive to blob");

        var accountNameOption = new Option<string>(
            alias: "--accountname",
            description: "Blob Account Name");
        accountNameOption.AddAlias("-n");
        accountNameOption.IsRequired = true;
        archiveCommand.AddOption(accountNameOption);

        Option accountKeyOption;
        //Inject from EnvironmentVariable, if it is defined
        var accountKeyEnvironmentVariable = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_KEY");
        if (string.IsNullOrEmpty(accountKeyEnvironmentVariable))
        {
            accountKeyOption = new Option<string>(
                alias: "--accountkey",
                description: "Account Key"); //TODO to --accountkey to const
            accountKeyOption.IsRequired = true;
        }
        else
        {
            accountKeyOption = new Option<string>(
                alias: "--accountkey", 
                description: "Account Key", 
                getDefaultValue: () => accountKeyEnvironmentVariable);
        }
        accountKeyOption.AddAlias("-k");
        archiveCommand.AddOption(accountKeyOption);

        var passphraseOption = new Option<string>(
            alias: "--passphrase",
            description: "Passphrase");
        passphraseOption.AddAlias("-p");
        passphraseOption.IsRequired = true;
        archiveCommand.AddOption(passphraseOption);

        var containerOption = new Option<string>(
            alias: "--container",
            getDefaultValue: () => "arius",
            description: "Blob container to use");
        containerOption.AddAlias("-c");
        archiveCommand.AddOption(containerOption);

        var removeLocalOption = new Option<bool>(
            alias: "--remove-local",
            description: "Remove local file after a successful upload");
        archiveCommand.AddOption(removeLocalOption);

        var tierOption = new Option<string>(
            alias: "--tier",
            getDefaultValue: () => "archive",
            description: "Storage tier to use. Defaut: archive");
        tierOption.AddValidator(o =>
        {
            // As per https://github.com/dotnet/command-line-api/issues/476#issuecomment-476723660
            var tier = o.GetValueOrDefault<string>();

            string[] tiers = { "hot", "cool", "archive" };
            if (!tiers.Contains(tier))
                return $"{tier} is not a valid tier (hot|cool|archive)";

            return string.Empty;
        });
        archiveCommand.AddOption(tierOption);

        var dedupOption = new Option<bool>(
            alias: "--dedup",
            getDefaultValue: () => false,
            description: "Deduplicate the chunks in the binary files"); //TODO better explanation
        archiveCommand.AddOption(dedupOption);

        var fastHashOption = new Option<bool>(
            alias: "--fasthash",
            getDefaultValue: () => false,
            description: "Use the cached hash of a file (faster, do not use in an archive where file contents change)");
        archiveCommand.AddOption(fastHashOption);

        Argument pathArgument;
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            pathArgument = new Argument<string>(
                name: "path",
                getDefaultValue: () => "/archive",
                description: "Path to archive.");
        }
        else
        {
            pathArgument = new Argument<string>(
                name: "path",
                //getDefaultValue: () => Environment.CurrentDirectory,
                //"Path to archive. Default: current directory");
                description: "Path to archive.");
        }
        archiveCommand.AddArgument(pathArgument);


        archiveCommand.Handler = CommandHandler
            .Create<string, string, string, string, bool, string, bool, bool, string, IHost>(
                async (accountName, accountKey, passphrase, container, removeLocal, tier, dedup, fastHash, path, host) =>
                {
                    ILogger logger = default;

                    try
                    {
                        logger = host.Services.GetRequiredService<ILogger<ArchiveCliCommand>>();

                        logger.LogInformation("Creating Facade...");
                        var facade = host.Services.GetRequiredService<IFacade>();

                        logger.LogInformation($@"Creating ArchiveCommand: archiving '{path}' to '{accountName}\{container}'...");
                        var c = facade.CreateArchiveCommand(accountName, accountKey, passphrase, fastHash, container, removeLocal, tier, dedup, path, versionUtc);

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

        return archiveCommand;
    }
}