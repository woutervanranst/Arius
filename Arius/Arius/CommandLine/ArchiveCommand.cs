using Azure.Storage.Blobs.Models;
using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;
using Arius.Extensions;
using Arius.Repositories;
using Arius.Services;

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
            (--remove-local)
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

            Option accountKeyOption;
            //Inject from EnvironmentVariable, if it is defined
            var accountKeyEnvironmentVariable = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_KEY");
            if (string.IsNullOrEmpty(accountKeyEnvironmentVariable))
            {
                accountKeyOption = new Option<string>(alias: "--accountkey", description: "Account Key"); //TODO to --accountkey to const
                accountKeyOption.IsRequired = true;
            }
            else
            {
                accountKeyOption = new Option<string>(alias: "--accountkey", description: "Account Key", getDefaultValue: () => accountKeyEnvironmentVariable);
            }
            accountKeyOption.AddAlias("-k");
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

            var removeLocalOption = new Option<bool>("--remove-local",
                "Remove local file after a successful upload");
            archiveCommand.AddOption(removeLocalOption);

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

            var dedupOption = new Option<bool>("--dedup",
                getDefaultValue: () => false,
                "Deduplicate the chunks in the binary files"); //TODO better explanation
            archiveCommand.AddOption(dedupOption);

            var fastHashOption = new Option<bool>("--fasthash",
                getDefaultValue: () => false,
                "Use the cached hash of a file (faster, do not use in an archive where file contents change)");
            archiveCommand.AddOption(fastHashOption);

            Argument pathArgument;
            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            {
                pathArgument = new Argument<string>("path",
                    getDefaultValue: () => "/archive");
                archiveCommand.AddArgument(pathArgument);
            }
            else
            { 
                pathArgument = new Argument<string>("path",
                    //getDefaultValue: () => Environment.CurrentDirectory,
                    //"Path to archive. Default: current directory");
                    "Path to archive.");
                archiveCommand.AddArgument(pathArgument);
            }

            archiveCommand.Handler = CommandHandlerExtensions
                .Create<string, string, string, string, bool, string, bool, bool, string>(
                    (accountName, accountKey, passphrase, container, removeLocal, tier, dedup, fastHash, path) =>
                    {
                        pcp.CommandExecutorType = typeof(ArchiveCommandExecutor);

                        pcp.CommandExecutorOptions = new ArchiveOptions()
                        {
                            AccountName = accountName,
                            AccountKey = accountKey,
                            Passphrase = passphrase,
                            FastHash = fastHash,
                            Container = container,
                            RemoveLocal = removeLocal,
                            Tier = tier,
                            Dedup = dedup,
                            Path = path
                        };

                        return Task.FromResult<int>(0);
                    });

            return archiveCommand;
        }
    }

    internal class ArchiveOptions : ICommandExecutorOptions,
        ISHA256HasherOptions, 
        IChunkerOptions, 
        IEncrypterOptions, 
        IAzCopyUploaderOptions,
        IConfigurationOptions,
        AzureRepository.IAzureRepositoryOptions
    {
        public string AccountName { get; init; }
        public string AccountKey { get; init; }
        public string Passphrase { get; init; }
        public bool FastHash { get; init; }
        public string Container { get; init; }
        public bool RemoveLocal { get; init; }
        public AccessTier Tier { get; init; } 
        //public int MinSize { get; init; }
        //public bool Simulate { get; init; }
        public bool Dedup { get; init; }
        public string Path { get; init; }
    }
}
