using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Text;

namespace Arius
{
    internal class RestoreCommand
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

            var downloadOption = new Option<bool>("--download",
                "List the differences between the local and the remote, without making any changes to remote");
            restoreCommand.AddOption(downloadOption);

            var pathArgument = new Argument<string>("path",
                getDefaultValue: () => Environment.CurrentDirectory,
                "Path to archive. Default: current directory");
            restoreCommand.AddArgument(pathArgument);

            //root.Handler = CommandHandler.Create<GreeterOptions, IHost>(Run);

            restoreCommand.Handler = CommandHandler.Create<string, string, string, string, bool, string>((accountName, accountKey, passphrase, container, download, path) =>
            {
                //var bu = new AriusRemoteArchive(accountName, accountKey, container);
                //var szu = new SevenZipUtils();

                //var rc = new RestoreCommand(szu, bu);
                //return rc.Execute(passphrase, download, path);

                return 0;
            });

            return restoreCommand;
        }

        public RestoreCommand(SevenZipUtils szu, AriusRemoteArchive bu)
        {
            _szu = szu;
            _bu = bu;
        }
        private readonly SevenZipUtils _szu;
        private readonly AriusRemoteArchive _bu;

        public int Execute(string passphrase, bool download, string path)
        {
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                Synchronize(di, passphrase);

                if (download)
                    Download(di, passphrase);
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

        public void Restore()
        {
            //var chunkFiles = chunks.Select(c => new FileStream(Path.Combine(clf.FullName, BitConverter.ToString(c.Hash)), FileMode.Open, FileAccess.Read));
            //var concaten = new ConcatenatedStream(chunkFiles);

            //var restorePath = Path.Combine(clf.FullName, "haha.exe");
            //using var fff = File.Create(restorePath);
            //concaten.CopyTo(fff);
            //fff.Close();
        }

        private int Synchronize(DirectoryInfo root, string passphrase)
        {
            //var cbn = _bu.GetContentBlobNames().ToArray();

            //Console.Write($"Getting {cbn.Length} manifests... ");
            //var cb = cbn.AsParallel().Select(contentBlobName => Manifest.GetManifest(_bu, _szu, contentBlobName, passphrase)).ToImmutableArray();

            //var remoteItems = cb
            //    .AsParallel()
            //    .Select(m => m.GetLocalAriusFiles(root))
            //    .SelectMany(laf => laf)
            //    .ToImmutableArray();
            //Console.WriteLine($"Done. {remoteItems.Count()} files in latest version of remote");


            //Console.WriteLine($"Synchronizing state of local folder with remote... ");

            //// 1. FILES THAT EXIST REMOTE BUT NOT LOCAL --> TO BE CREATED
            //var ariusFilesToCreate = remoteItems.Where(laf => !laf.Exists);
            //Console.WriteLine($"{ariusFilesToCreate.Count()} file(s) present in latest version of remote but not locally... Creating...");
            //ariusFilesToCreate.AsParallel().ForAll(laf =>
            //{
            //    laf.Create();
            //    Console.WriteLine($"File '{laf.RelativeAriusFileName}' created");
            //});

            //Console.WriteLine();

            //// 2. FILES THAT EXIST LOCAL BUT NOT REMOTE --> TO BE DELETED
            //var ariusFilesToDelete = root.GetFiles("*.arius", SearchOption.AllDirectories)
            //    .Select(localFileInfo => localFileInfo.FullName)
            //    .Except(remoteItems.Select(laf => laf.AriusFileName))
            //    .ToImmutableArray();
            //Console.WriteLine($"{ariusFilesToDelete.Count()} file(s) not present in latest version of remote... Deleting...");
            //ariusFilesToDelete.AsParallel().ForAll(filename =>
            //{
            //    File.Delete(filename);

            //    //TODO Delete empty directories / recursively

            //    Console.WriteLine($"File '{Path.GetRelativePath(root.FullName, filename)}' deleted");
            //});




            ///*
            // * Test cases
            // *      empty dir
            // *      dir with files > not to be touched?
            // *      dir with pointers - too many pointers > to be deleted
            // *      dir with pointers > not enough pointers > to be synchronzed
            // *      remote with isdeleted and local present > should be deleted
            // *      remote with !isdeleted and local not present > should be created
            // *      also in subdirectories
            // *      in ariusfile : de verschillende extensions
            // *      files met duplicates enz upload download
            // *      al 1 file lokaal > kopieert de rest
            // *      restore > normal binary file remains untouched
            // * */


            return 0;
        }

        private int Download(DirectoryInfo root, string passphrase)
        {
            //var blobsToDownload = root.GetFiles("*.arius", SearchOption.AllDirectories)
            //    .AsParallel()
            //    .WithDegreeOfParallelism(1)
            //    .Select(localFileInfo => new LocalAriusFileWithoutManifest(root, Path.GetRelativePath(root.FullName, localFileInfo.FullName)))
            //    .GroupBy(lafwm => lafwm.ContentBlobName, lafwm => lafwm).Select(g => g.ToImmutableArray());

            //blobsToDownload
            //    .AsParallel()
            //    .WithDegreeOfParallelism(1)
            //    .ForAll(blobGroup =>
            //    {
            //        var contentFileName = blobGroup.First().ContentFileName;

            //        if (File.Exists(contentFileName))
            //        {
            //            //Already exists, check hash
            //            var hash = FileUtils.GetHash(passphrase, contentFileName);

            //            if (hash != blobGroup.First().Hash)
            //                //Hash differs > delete file
            //                File.Delete(contentFileName);
            //        }

            //        if (!File.Exists(contentFileName))
            //        {
            //            //Download
            //            var tempContentFileName = blobGroup.First().ContentBlob.Download(_bu, _szu, passphrase);
            //            File.Move(tempContentFileName, contentFileName);
            //        }

            //        blobGroup.Skip(1).AsParallel().ForAll(lafwm =>
            //        {
            //            // Copy & overwrite
            //            File.Copy(contentFileName, lafwm.ContentFileName, true);

            //            //TODO https://docs.microsoft.com/en-us/dotnet/api/system.io.file.setlastwritetime?view=netcore-3.1
            //        });
            //    });

            return 0;
        }
    }
}
