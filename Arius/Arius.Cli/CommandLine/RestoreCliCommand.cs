using Arius.Core.Facade;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace Arius.CommandLine
{
    internal  static class RestoreCliCommand
    {
        public static Command GetCommand()
        {
            var restoreCommand = new Command("restore", "Restore from blob");

            var accountNameOption = new Option<string>("--accountname",
                "Blob Account Name");
            accountNameOption.AddAlias("-n");
            accountNameOption.IsRequired = true;
            restoreCommand.AddOption(accountNameOption);

            var accountKeyOption = new Option<string>("--accountkey",
                "Account Key");
            accountKeyOption.AddAlias("-k");
            accountKeyOption.IsRequired = true;
            restoreCommand.AddOption(accountKeyOption);

            var passphraseOption = new Option<string>("--passphrase",
                "Passphrase");
            passphraseOption.AddAlias("-p");
            passphraseOption.IsRequired = true;
            restoreCommand.AddOption(passphraseOption);

            var containerOption = new Option<string>("--container",
                getDefaultValue: () => "arius",
                description: "Blob container to use");
            containerOption.AddAlias("-c");
            restoreCommand.AddOption(containerOption);

            var syncOption = new Option<bool>("--synchronize",
                "Create pointers on local for every remote file, without actually downloading the files");
            restoreCommand.AddOption(syncOption);

            var downloadOption = new Option<bool>("--download",
                "Download file files for the given pointer in <path> (file) or all the pointers in <path> (folder)");
            restoreCommand.AddOption(downloadOption);

            var keepPointersOption = new Option<bool>("--keep-pointers",
                getDefaultValue: () => false,
                "Keep pointer files after downloading content files");
            restoreCommand.AddOption(keepPointersOption);

            Argument pathArgument;
            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            {
                pathArgument = new Argument<string>("path",
                    getDefaultValue: () => "/archive");
                restoreCommand.AddArgument(pathArgument);
            }
            else
            {
                pathArgument = new Argument<string>("path",
                    //getDefaultValue: () => Environment.CurrentDirectory,
                    //"Path to archive. Default: current directory");
                    "Path to archive.");
                restoreCommand.AddArgument(pathArgument);
            }

            restoreCommand.Handler = CommandHandler
                .Create<string, string, string, string, bool, bool, bool, string, Microsoft.Extensions.Hosting.IHost>(
                    async (accountName, accountKey, passphrase, container, synchronize, download, keepPointers, path, host) =>
                    {
                        var facade = host.Services.GetRequiredService<IFacade>();
                        var c = facade.CreateRestoreCommand(accountName, accountKey, container, passphrase, synchronize, download, keepPointers, path);
                        //var c = facade.CreateRestoreCommand(o);

                        return await c.Execute();

                        //pcp.CommandExecutorType = typeof(RestoreCommandExecutor);

                        //pcp.CommandExecutorOptions = new RestoreOptions
                        //{
                        //    AccountName = accountName,
                        //    AccountKey = accountKey,
                        //    Passphrase = passphrase,
                        //    Container = container,
                        //    Synchronize = synchronize,
                        //    Download = download,
                        //    KeepPointers = keepPointers,
                        //    Path = path
                        //};

                        //return Task.FromResult<int>(0);
                    });

            return restoreCommand;
        }
    }
}