using Azure.Storage.Blobs.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.CommandLine;
using Microsoft.Extensions.Logging;

namespace Arius.CommandLine
{
    internal class ArchiveCommand : IAriusCommand
    {
        /*
        *  arius archive 
            --accountname <accountname> 
            --accountkey <accountkey> 
            --passphrase <passphrase>
            (--container <containername>) 
            (--keep-local)
            (--tier=(hot/cool/archive))
            (--min-size=<minsizeinMB>)
            (--simulate)
        * */

        public Command GetCommand(ParsedCommandProvider pcp)
        {
            var archiveCommand = new Command("archive", "Archive to blob");

            var accountNameOption = new Option<string>("--accountname",
                "Blob Account Name");
            accountNameOption.AddAlias("-n");
            accountNameOption.IsRequired = true;
            archiveCommand.AddOption(accountNameOption);


            var accountKeyOption = new Option<string>("--accountkey",
                "Account Key");
            accountKeyOption.AddAlias("-k");
            accountKeyOption.IsRequired = true;
            archiveCommand.AddOption(accountKeyOption);

            var passphraseOption = new Option<string>("--passphrase",
                "Passphrase");
            passphraseOption.AddAlias("-p");
            passphraseOption.IsRequired = true;
            archiveCommand.AddOption(passphraseOption);

            var containerOption = new Option<string>("--container",
                getDefaultValue: () => "arius",
                description: "Blob container to use");
            containerOption.AddAlias("-c");
            archiveCommand.AddOption(containerOption);

            var keepLocalOption = new Option<bool>("--keep-local",
                "Do not delete the local copies of the file after a successful upload");
            archiveCommand.AddOption(keepLocalOption);

            var tierOption = new Option<string>("--tier",
                getDefaultValue: () => "archive",
                description: "Storage tier to use. Defaut: archive");
            tierOption.AddValidator(o =>
            {
                // As per https://github.com/dotnet/command-line-api/issues/476#issuecomment-476723660
                var tier = o.GetValueOrDefault<string>();

                string[] tiers = {"hot", "cool", "archive"};
                if (!tiers.Contains(tier))
                    return $"{tier} is not a valid tier (hot|cool|archive)";

                return string.Empty;
            });
            archiveCommand.AddOption(tierOption);

            var minSizeOption = new Option<int>("--min-size",
                getDefaultValue: () => 0,
                description: "Minimum size of files to archive in MB");
            archiveCommand.AddOption(minSizeOption);

            var simulateOption = new Option<bool>("--simulate",
                "List the differences between the local and the remote, without making any changes to remote");
            archiveCommand.AddOption(simulateOption);

            var pathArgument = new Argument<string>("path",
                getDefaultValue: () => Environment.CurrentDirectory,
                "Path to archive. Default: current directory");
            archiveCommand.AddArgument(pathArgument);


            archiveCommand.Handler = CommandHandlerExtensions
                .Create<string, string, string, string, bool, string, int, bool, string>(
                    (accountName, accountKey, passphrase, container, keepLocal, tier, minSize, simulate, path) =>
                    {
                        pcp.CommandExecutorType = typeof(ArchiveCommandExecutor);

                        pcp.CommandExecutorOptions = new ArchiveOptions()
                        {
                            AccountName = accountName,
                            AccountKey = accountKey,
                            Passphrase = passphrase,
                            Container = container,
                            KeepLocal = keepLocal,
                            Tier = tier,
                            MinSize = minSize,
                            Simulate = simulate,
                            Path = path
                        };

                        return Task.FromResult<int>(0);
                    });

            return archiveCommand;
        }
    }

    internal struct ArchiveOptions : ILocalRootDirectoryOptions, 
        ISHA256HasherOptions, 
        IChunkerOptions, 
        IEncrypterOptions, 
        IAzCopyUploaderOptions, 
        IRemoteContainerRepositoryOptions, 
        IConfigurationOptions,
        IManifestRepositoryOptions,
        IRemoteChunkRepositoryOptions
    {
        public string AccountName { get; init; }
        public string AccountKey { get; init; }
        public string Passphrase { get; init; }
        public string Container { get; init; }
        public bool KeepLocal { get; init; }
        public AccessTier Tier { get; init; } 
        public int MinSize { get; init; }
        public bool Simulate { get; init; }
        public bool Dedup { get; init; }
        public string Path { get; init; }
    }

    internal class ArchiveCommandExecutor  : ICommandExecutor
    {
        private readonly ILogger<ArchiveCommandExecutor> _logger;
        private readonly ILocalRepository _localRoot;
        private readonly IRemoteRepository _remoteArchive;

        public ArchiveCommandExecutor(ICommandExecutorOptions options,
            ILogger<ArchiveCommandExecutor> logger,
            ILocalRepository localRoot,
            IRemoteRepository remoteArchive)
        {
            _logger = logger;
            _localRoot = localRoot;
            _remoteArchive = remoteArchive;
        }
        public int Execute()
        {
            var allLocalFiles = _localRoot.GetAll();
            _remoteArchive.PutAll(allLocalFiles);

            return 0;
        }





    }
}
