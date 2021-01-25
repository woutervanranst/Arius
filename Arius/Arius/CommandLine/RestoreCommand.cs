using System;
using System.CommandLine;
using System.Threading.Tasks;
using Arius.Extensions;
using Arius.Repositories;
using Arius.Services;
using Azure.Storage.Blobs.Models;

namespace Arius.CommandLine
{
    internal class RestoreCommand : IAriusCommand
    {
        /*
         * arius restore
               --accountname <accountname> 
               --accountkey <accountkey> 
               --passphrase <passphrase>
              (--download)
         */

        Command IAriusCommand.GetCommand(ParsedCommandProvider pcp)
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

            restoreCommand.Handler = CommandHandlerExtensions.Create<string, string, string, string, bool, bool, bool, string>((accountName, accountKey, passphrase, container, synchronize, download, keepPointers, path) =>
            {
                pcp.CommandExecutorType = typeof(RestoreCommandExecutor);

                pcp.CommandExecutorOptions = new RestoreOptions
                {
                    AccountName = accountName,
                    AccountKey = accountKey,
                    Passphrase = passphrase,
                    Container = container,
                    Synchronize = synchronize,
                    Download = download,
                    KeepPointers = keepPointers,
                    Path = path
                };

                return Task.FromResult<int>(0);
            });

            return restoreCommand;
        }
    }

    internal class RestoreOptions : ICommandExecutorOptions,
        IChunkerOptions,
        ISHA256HasherOptions,
        IAzCopyUploaderOptions,
        IEncrypterOptions,
        AzureRepository.IAzureRepositoryOptions
    {
        public string AccountName { get; init; }
        public string AccountKey { get; init; }
        public string Passphrase { get; init; }
        public bool FastHash { get; init; }
        public string Container { get; init; }
        public bool Synchronize { get; init; }
        public bool Download { get; init; }
        public bool KeepPointers { get; init; }
        public string Path { get; init; }

        public bool Dedup => false;
        public AccessTier Tier { get => throw new NotImplementedException(); init => throw new NotImplementedException(); } // Should not be used
        public bool RemoveLocal { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }
        public int MinSize { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }
        public bool Simulate { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }
    }
}