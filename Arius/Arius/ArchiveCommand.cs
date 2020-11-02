using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius
{
    class ArchiveCommand
    {
        public ArchiveCommand(SevenZipUtils szu)
        {
            _szu = szu;
        }
        private readonly SevenZipUtils _szu;

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
        public Command GetArchiveCommand()
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

                string[] tiers = { "hot", "cool", "archive" };
                if (!tiers.Contains(tier))
                    return $"{tier} is not a valid tier (hot|cool|archive)";

                return string.Empty;
            });
            archiveCommand.AddOption(tierOption);

            var minSizeOption = new Option<int>("--min-size",
                getDefaultValue: () => 1,
                description: "Minimum size of files to archive in MB");
            archiveCommand.AddOption(minSizeOption);

            var simulateOption = new Option<bool>("--simulate",
                "List the differences between the local and the remote, without making any changes to remote");
            archiveCommand.AddOption(simulateOption);

            var pathArgument = new Argument<string>("path",
                getDefaultValue: () => Environment.CurrentDirectory,
                "Path to archive. Default: current directory");
            archiveCommand.AddArgument(pathArgument);

            ArchiveDelegate archiveCommandHandler = Execute;

            archiveCommand.Handler = CommandHandler.Create(archiveCommandHandler);

            return archiveCommand;
        }

        delegate int ArchiveDelegate(string accountName, string accountKey, string passphrase, string container, bool keepLocal, string tier, int minSize, bool simulate, string path);

        private int Execute(string accountName, string accountKey, string passphrase, string container, bool keepLocal, string tier, int minSize, bool simulate, string path)
        {
            var bu = new BlobUtils(accountName, accountKey, container);

            var di = new DirectoryInfo(path);

            var accessTier = tier switch
            {
                "hot" => AccessTier.Hot,
                "cool" => AccessTier.Cool,
                "archive" => AccessTier.Archive,
                _ => throw new NotImplementedException()
            };

            return Execute(passphrase, bu, keepLocal, accessTier, minSize, simulate, di);
        }

        private int Execute(string passphrase, BlobUtils bu, bool keepLocal, AccessTier tier, int minSize, bool simulate, DirectoryInfo dir)
        {
            foreach (var fi in dir.GetFiles("*.*", SearchOption.AllDirectories))
            {
                if (fi.Length < minSize * 1024 * 1024)
                    continue;

                if (fi.Name.EndsWith(".arius"))
                    continue;


                Console.WriteLine($"Archiving file: {fi.FullName.Replace(dir.FullName, "")}");
                var source = fi.FullName;
                var encryptedSource = Path.Combine(fi.DirectoryName, $"{fi.Name}.7z.arius");
                var blobTarget = $"{Guid.NewGuid()}.7z.arius";
                var localTarget = Path.Combine(fi.DirectoryName, $"{fi.Name}.arius");

                Console.Write("Encrypting... ");
                _szu.Encrypt(source, encryptedSource, passphrase);
                Console.WriteLine("Done");

                Console.Write("Uploading... ");
                bu.Upload(encryptedSource, blobTarget, tier);
                Console.WriteLine("Done");

                Console.Write("Creating local pointer... ");
                File.WriteAllText(localTarget, blobTarget, Encoding.UTF8);
                Console.WriteLine("Done");

                if (keepLocal)
                {
                    throw new NotImplementedException();
                }
                else
                {
                    Console.Write("Deleting local file... ");
                    File.Delete(source);
                    Console.WriteLine("Done");
                }

                File.Delete(encryptedSource);


                Console.WriteLine("");
            }

            return 0;
        }
    }
}
