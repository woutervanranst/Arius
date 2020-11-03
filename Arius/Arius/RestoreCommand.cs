using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Text;

namespace Arius
{
    class RestoreCommand
    {
        /*
         * arius restore
               --accountname <accountname> 
               --accountkey <accountkey> 
               --passphrase <passphrase>
              (--download)
         */

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

            //var downloadOption = new Option<bool>("--download",
            //    "List the differences between the local and the remote, without making any changes to remote");
            //restoreCommand.AddOption(downloadOption);

            var pathArgument = new Argument<string>("path",
                getDefaultValue: () => Environment.CurrentDirectory,
                "Path to archive. Default: current directory");
            restoreCommand.AddArgument(pathArgument);

            //root.Handler = CommandHandler.Create<GreeterOptions, IHost>(Run);

            restoreCommand.Handler = CommandHandler.Create<string, string, string, string, string>((accountName, accountKey, passphrase, container, path) =>
            {
                var bu = new BlobUtils(accountName, accountKey, container);
                var szu = new SevenZipUtils();

                var rc = new RestoreCommand(szu, bu);
                return rc.Execute(passphrase, path);
            });

            return restoreCommand;
        }

        public RestoreCommand(SevenZipUtils szu, BlobUtils bu)
        {
            _szu = szu;
            _bu = bu;
        }
        private readonly SevenZipUtils _szu;
        private readonly BlobUtils _bu;

        public int Execute(string passphrase, string path)
        {
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                if (di.EnumerateFiles().Any())
                {
                    // Non empty directory > restore all files
                }
                else
                {
                    // Empty directory > restore all pointers
                    RestorePointers(passphrase, di);
                }
            } 
            else if (File.Exists(path) && path.EndsWith(".arius"))
            {
                // Restore one file

            }
            else
            {
                throw new NotImplementedException();
            }
            return 0;
        }

        private int RestorePointers(string passphrase, DirectoryInfo dir)
        {
            foreach (var contentBlobName in _bu.GetContentBlobNames())
            {
                var manifest = Manifest.GetManifest(_bu, _szu, contentBlobName, passphrase);

                var entriesPerFileName = manifest.Entries.GroupBy(me => me.RelativeFileName, me => me);
                foreach (var me in entriesPerFileName)
                {
                    var lastEntry = me.OrderBy(mm => mm.DateTime).Last();

                    if (!lastEntry.IsDeleted)
                    {
                        var localFile = new FileInfo(Path.Combine(dir.FullName, $"{me.Key}.arius"));
                        if (!localFile.Directory.Exists)
                            localFile.Directory.Create();

                        File.WriteAllText(localFile.FullName, contentBlobName);
                    }
                }
            }

            return 0;
        }
    }

}
