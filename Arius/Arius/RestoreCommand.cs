﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
                var bu = new BlobUtils(accountName, accountKey, container);
                var szu = new SevenZipUtils();

                var rc = new RestoreCommand(szu, bu);
                return rc.Execute(passphrase, download, path);
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

        public int Execute(string passphrase, bool download, string path)
        {
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                //if (di.EnumerateFiles().Any())
                //{
                //    // Non empty directory > restore all files
                //}
                //else
                //{
                //    // Empty directory > restore all pointers
                    RestorePointers(passphrase, di);
                //}
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

        private int RestorePointers(string passphrase, DirectoryInfo root)
        {
            //foreach (var contentBlobName in _bu.GetContentBlobNames())
            //{
            //    var manifest = Manifest.GetManifest(_bu, _szu, contentBlobName, passphrase);

            //    var entriesPerFileName = manifest.Entries
            //        .GroupBy(me => me.RelativeFileName, me => me)
            //        .Select(meg => meg.OrderBy(me => me.DateTime).Last());

            //    foreach (var me in entriesPerFileName)
            //    {
            //        if (!me.IsDeleted)
            //        {
            //            var localFile = new FileInfo(Path.Combine(dir.FullName, $"{me.RelativeFileName}.arius"));
            //            if (!localFile.Directory.Exists)
            //                localFile.Directory.Create();

            //            File.WriteAllText(localFile.FullName, contentBlobName);
            //        }
            //    }
            //}

            var cbn = _bu.GetContentBlobNames().ToArray();

            Console.Write($"Getting {cbn.Length} manifests... ");
            var cb = cbn.AsParallel().Select(contentBlobName => Manifest.GetManifest(_bu, _szu, contentBlobName, passphrase)).ToImmutableArray();

            var remoteItems = cb
                .AsParallel()
                .Select(m => m.GetLocalAriusFiles(root))
                .SelectMany(laf => laf)
                .ToImmutableArray();
            Console.WriteLine($"Done. {remoteItems.Count()} files in latest version of remote");

            
            Console.WriteLine($"Synchronizing state of local folder with remote... ");
            
            // 1. FILES THAT EXIST REMOTE BUT NOT LOCAL --> TO BE CREATED
            var ariusFilesToCreate = remoteItems.Where(laf => !laf.Exists);
            Console.WriteLine($"{ariusFilesToCreate.Count()} files to be created... ");
            ariusFilesToCreate.AsParallel().ForAll(laf =>
            {
                laf.Create();
                Console.WriteLine($"File '{laf.RelativeAriusFileName}' created");
            });

            // 2. FILES THAT EXIST LOCAL BUT NOT REMOTE --> TO BE DELETED
            var ariusFilesToDelete = root.GetFiles("*.arius", SearchOption.AllDirectories)
                .Select(localFileInfo => localFileInfo.FullName)
                .Except(remoteItems.Select(laf => laf.AriusFileName))
                .ToImmutableArray();
            Console.WriteLine($"File '{ariusFilesToDelete.Count()}' files to be deleted... ");
            ariusFilesToDelete.AsParallel().ForAll(filename =>
            {
                File.Delete(filename);
                Console.WriteLine($"{Path.GetRelativePath(root.FullName, filename)} deleted");
            });

            //Create Files

            //Delete Files



            //foreach (var fi in root.GetFiles("*.arius", SearchOption.AllDirectories))
            //{
            //    var relativeAriusFileName = Path.GetRelativePath(root.FullName, fi.FullName);

            //    if (remoteItems.ContainsKey(relativeAriusFileName))
            //        // File exists locally and should exist as per latest remote
            //        remoteItems[relativeAriusFileName].Value = true;
            //    else
            //    {
            //        Console.Write($"File '{relativeAriusFileName}' not present in latest version of remote... Deleting... ");
            //        // File exists locally and should not exist as per latest remote
            //        fi.Delete();
            //        Console.WriteLine("Done");
            //    }
            //}
            //foreach (var syncItem in syncItems.Where(syncItem => !syncItem.Value.Checked))
            //{
            //    Console.Write($"File '{syncItem.Value}' present in latest version of remote but not locally... Creating... ");
            //    // File exists as per remote but does not exist locally
            //    LocalAriusFile.CreatePointer(Path.Combine(root.FullName, $"{syncItem.Value.RelativeFileName}.arius"), syncItem.Value.ContentBlobName);
            //    Console.WriteLine("Done");
            //}






            //foreach (var fi in root.GetFiles("*.arius", SearchOption.AllDirectories))
            //{
            //    var relativeLocalContentFileName = LocalAriusFile.GetLocalContentName(Path.GetRelativePath(root.FullName, fi.FullName));

            //    if (syncItems.ContainsKey(relativeLocalContentFileName))
            //        // File exists locally and should exist as per latest remote
            //        syncItems[relativeLocalContentFileName].Checked = true;
            //    else
            //    {
            //        Console.Write($"File '{relativeLocalContentFileName}' not present in latest version of remote... Deleting... ");
            //        // File exists locally and should not exist as per latest remote
            //        fi.Delete();
            //        Console.WriteLine("Done");
            //    }
            //}
            //foreach (var syncItem in syncItems.Where(syncItem => !syncItem.Value.Checked))
            //{
            //    Console.Write($"File '{syncItem.Value}' present in latest version of remote but not locally... Creating... ");
            //    // File exists as per remote but does not exist locally
            //    LocalAriusFile.CreatePointer(Path.Combine(root.FullName, $"{syncItem.Value.RelativeFileName}.arius"), syncItem.Value.ContentBlobName);
            //    Console.WriteLine("Done");
            //}

            /*
             * Test cases
             *      empty dir
             *      dir with files > not to be touched?
             *      dir with pointers - too many pointers > to be deleted
             *      dir with pointers > not enough pointers > to be synchronzed
             *      remote with isdeleted and local present > should be deleted
             *      remote with !isdeleted and local not present > should be created
             *      also in subdirectories
             * */

            Console.WriteLine("Done");

            return 0;
        }

        
    }

    internal class SyncItem
    {
        public string RelativeFileName { get; set; }
        public string ContentBlobName { get; set; }
        public bool Checked { get; set; }
    }
}
