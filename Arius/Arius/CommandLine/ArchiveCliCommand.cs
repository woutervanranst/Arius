using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;
using Arius.Extensions;

namespace Arius.CommandLine
{
    internal class ArchiveCliCommand : ICliCommand
    {
        public ArchiveCliCommand(Arius.Core.Facade.Facade facade)
        {
            this.facade = facade;
        }

        private readonly Arius.Core.Facade.Facade facade;

        public Command GetCommand()
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
                    async (accountName, accountKey, passphrase, container, removeLocal, tier, dedup, fastHash, path) =>
                    {
                        var o = new Core.Commands.ArchiveCommandOptions()
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

                        var c = facade.CreateArchiveCommand(o);

                        return await c.Execute();
                        

                        //pcp.CommandExecutorType = typeof(ArchiveCommandExecutor);

                        //pcp.CommandExecutorOptions = new ArchiveOptions()
                        //{
                        //    AccountName = accountName,
                        //    AccountKey = accountKey,
                        //    Passphrase = passphrase,
                        //    FastHash = fastHash,
                        //    Container = container,
                        //    RemoveLocal = removeLocal,
                        //    Tier = tier,
                        //    Dedup = dedup,
                        //    Path = path
                        //};

                        //return Task.FromResult<int>(0);
                    });

            return archiveCommand;
        }
    }
}
