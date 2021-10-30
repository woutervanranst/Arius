using Arius.Core.Configuration;
using Arius.Core.Facade;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace Arius.Cli.CommandLine;

internal class RestoreCliCommand : ICliCommand
{
    public Command GetCommand()
    {
        var restoreCommand = new Command(
            name: "restore", 
            description: "Restore from blob");

        var accountNameOption = new Option<string>(
            alias: "--accountname",
            description: "Blob Account Name");
        accountNameOption.AddAlias("-n");
        accountNameOption.IsRequired = true;
        restoreCommand.AddOption(accountNameOption);

        var accountKeyOption = new Option<string>(
            alias: "--accountkey",
            description: "Account Key");
        accountKeyOption.AddAlias("-k");
        accountKeyOption.IsRequired = true;
        restoreCommand.AddOption(accountKeyOption);

        var passphraseOption = new Option<string>(
            alias: "--passphrase",
            description: "Passphrase");
        passphraseOption.AddAlias("-p");
        passphraseOption.IsRequired = true;
        restoreCommand.AddOption(passphraseOption);

        var containerOption = new Option<string>(
            alias: "--container",
            getDefaultValue: () => "arius",
            description: "Blob container to use");
        containerOption.AddAlias("-c");
        restoreCommand.AddOption(containerOption);

        var syncOption = new Option<bool>(
            alias: "--synchronize",
            description: "Create pointers on local for every remote file, without actually downloading the files");
        restoreCommand.AddOption(syncOption);

        var downloadOption = new Option<bool>(
            alias: "--download",
            description: "Download file files for the given pointer in <path> (file) or all the pointers in <path> (folder)");
        restoreCommand.AddOption(downloadOption);

        var keepPointersOption = new Option<bool>(
            alias: "--keep-pointers",
            getDefaultValue: () => false,
            description: "Keep pointer files after downloading content files");
        restoreCommand.AddOption(keepPointersOption);

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
        restoreCommand.AddArgument(pathArgument);


        restoreCommand.Handler = CommandHandler
            .Create<string, string, string, string, bool, bool, bool, string, IHost>(
                async (accountName, accountKey, passphrase, container, synchronize, download, keepPointers, path, host) =>
                {
                    ILogger logger = default;

                    try
                    {
                        logger = host.Services.GetRequiredService<ILogger<RestoreCliCommand>>();

                        logger.LogInformation("Creating Facade...");
                        var facade = host.Services.GetRequiredService<IFacade>();

                        logger.LogInformation($@"Creating RestoreCommand: restoring '{accountName}\{container}' to '{path}'...");
                        var c = facade.CreateRestoreCommand(accountName, accountKey, container, passphrase, synchronize, download, keepPointers, path);

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

        return restoreCommand;
    }
}