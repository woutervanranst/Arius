﻿using Azure.Storage.Blobs.Models;
using MoreLinq;
using System;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;

namespace Arius
{
    internal class ArchiveCommand
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
        public static Command GetCommand()
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

            ArchiveDelegate archiveCommandHandler = Execute;

            archiveCommand.Handler = CommandHandler.Create(archiveCommandHandler); //TODO Delegate kan weg

            return archiveCommand;
        }

        private delegate int ArchiveDelegate(string accountName, string accountKey, string passphrase, string container, bool keepLocal, string tier, int minSize, bool simulate, string path);

        private static int Execute(string accountName, string accountKey, string passphrase, string container, bool keepLocal, string tier, int minSize, bool simulate, string path)
        {
            var accessTier = tier switch
            {
                "hot" => AccessTier.Hot,
                "cool" => AccessTier.Cool,
                "archive" => AccessTier.Archive,
                _ => throw new NotImplementedException()
            };

            var archive = new AriusRemoteArchive(accountName, accountKey, container);
            var root = new AriusRootDirectory(path);

            return Archive(root, archive, passphrase, keepLocal, accessTier, minSize, simulate);

        }

        public static int Archive(AriusRootDirectory root, AriusRemoteArchive archive, string passphrase, bool keepLocal, AccessTier tier, int minSize, bool simulate)
        {

            ////TODO KeepLocal
            ////TODO Simulate
            //// TODO MINSIZE
            ///
            /// TODO CHeck if the archive is deduped and password by checking the first amnifest file

            /* 1. LocalContentFiles (ie. all non-.arius files)
             *  READ > N/A
             *  CREATE > uploaden en manifest schrijven
             *  UPDATE > uploaden en manifest overschrijven
             *  DELETE > N/A
             */

            //1.1 Ensure all binaries are uploaded
            var localContentPerHash = root
                    .GetNonAriusFiles()
                    .AsParallel()
                        .WithDegreeOfParallelism(1)
                    .Select(fi => new LocalContentFile(root, fi, passphrase))
                    .GroupBy(lcf => lcf.Hash)
                    .ToImmutableArray();

            var remoteContentHashes = archive.GetContentHashes();

            var localContentFilesToUpload = localContentPerHash
                .Where(g => !remoteContentHashes.Contains(g.Key))
                .Select(g => g.First())
                .ToImmutableArray();

            var unencryptedChunksPerLocalContentFile = localContentFilesToUpload
                .AsParallel()
                    .WithDegreeOfParallelism(1)
                .ToImmutableDictionary(
                    lcf => lcf,
                    lcf => lcf.GetChunks(false));

            var encryptedChunksPerHash = unencryptedChunksPerLocalContentFile.Values
                .AsParallel()
                .WithDegreeOfParallelism(1)
                .SelectMany(lcf => lcf)
                .DistinctBy(uec => uec.Hash)
                .ToImmutableDictionary(
                    uec => uec.Hash,
                    uec => uec.GetEncryptedAriusChunk(passphrase));
            // For dedup --> . EXCEPT ALREADY REMOTE TODO archive.GetEncryptedAriusChunkBlobItems

            archive.Upload(encryptedChunksPerHash.Values, tier);

            //Delete Uploaded Chunks
            foreach (var chunk in encryptedChunksPerHash.Values)
                File.Delete(chunk.FullName);


            //1.2 Create manifests & pointers

            var encryptedManifestsToUpload = localContentFilesToUpload
                .AsParallel()
                .WithDegreeOfParallelism(1)
                .Select(lcf =>
                {
                    var eamf = lcf.GetManifest(encryptedChunksPerHash[lcf.Hash])
                        .CreateAriusManifestFile(lcf.AriusManifestFullName)
                        .CreateEncryptedAriusManifestFile(lcf.EncryptedAriusManifestFullName, passphrase, true);

                    return eamf;
                })
                .ToImmutableArray();


            archive.Upload(encryptedManifestsToUpload);

            //Delete Uploaded Manifests
            foreach (var manifestFile in encryptedManifestsToUpload)
                File.Delete(manifestFile.FullName);

            // TODO DAN: QUID  AriusPointerFile.Create ??


            //1.2 Create pointers & update the remote manifests
            var x = 5;

            var remainingLocalContentPerManifestFile = localContentPerHash
                .ToImmutableDictionary(
                    g => archive.GetRemoteEncryptedAriusManifestFileByHash(g.Key),
                    g => g.Where(z => !z.AriusPointerFileInfo.Exists).ToList());

            var pointers = remainingLocalContentPerManifestFile
                .Where(p => p.Value.Count > 0) //hack
                .Select(p => p.Key.CreatePointers(p.Value, passphrase))
                .ToImmutableArray();

            /*
             * FROM                             TO
             * hash1    content1                hash1   content1
             *          content2                hash1   content2                
             *          content3                hash1   content3
             * hash2    content4                hash2   content4
             *          content5                hash2   content5
             */



            //var pointersToCreate = remainingLocalContentPerManifestFile
            //    .SelectMany(p => p.Value.Select(lcf => new
            //    {
            //        RemoteEncryptedAriusManifestFile = p.Key, 
            //        LocalContentFile = lcf
            //    }))
            //    .Where(z => !z.LocalContentFile.AriusPointerFileInfo.Exists)
            //    .ToImmutableArray();

            //var createdPointerFiles = pointersToCreate 
            //    .AsParallel()
            //        .WithDegreeOfParallelism(1)
            //    .Select(z => AriusPointerFile.Create(z.LocalContentFile, z.RemoteEncryptedAriusManifestFile))
            //    .ToImmutableArray();



            /* 2. Local AriusPointerFiles (.arius files of LocalContentFiles that were not touched in #1) --- de OVERBLIJVENDE .arius files
             * CREATE >N/A
             * READ > N/A
             * UPDATE > remote manifest bijwerken (naming, plaats, ;;;)
             * DELETE > remote manifest bijwerken
             */

            //var remainingAriusPointers = root
            //    .GetAriusFiles()
            //    .Select(fi => AriusPointerFile.Create(fi))
            //    .ExceptBy(encryptedAriusContentsToUpload.Select(eac => eac.PointerFile), pf => pf.FullName)
            //    .ToImmutableArray();

            //var x = 5;

            return 0;


            // DO STUFF
            // AsParallel

            /* 3. Remote Manifests that were not touched by #1 or #2 --- Dan de OVERBLIJVENDE remote manifest files
             * CREATE > N/A
             * READ > N/A
             * UPDATE > N/A
             * DELETE > if deleted local > delete
             */

            //var kaka = archive.GetEncryptedManifestNames()
            //    .Except(localContentPerHash.Select(lcf => lcf.EncryptedManifestFile.Name))
            //    .Except(remainingAriusPointers.Select(apf => apf.EncryptedManifestName));



            //    foreach (var contentBlobName in _bu.GetContentBlobNames())
            //    {
            //        var manifest = Manifest.GetManifest(_bu, _szu, contentBlobName, passphrase);
            //        var entries = manifest.GetLastEntries(true);

            //        bool toUpdate = false;

            //        foreach (var me in entries)
            //        {
            //            var localFile = Path.Combine(dir.FullName, me.RelativeFileName);

            //            if (!me.IsDeleted && !File.Exists(localFile) && !File.Exists($"{localFile}.arius"))
            //            {
            //                // DELETE - File is deleted
            //                manifest.AddEntry(me.RelativeFileName, true);
            //                toUpdate = true;

            //                Console.ForegroundColor = ConsoleColor.Red;
            //                Console.WriteLine($"File {me.RelativeFileName} is deleted. Marking as deleted on remote...");
            //                Console.ResetColor();
            //            }
            //        }

            //        if (toUpdate)
            //        {
            //            manifest.Archive(_bu, _szu, passphrase);
            //            Console.WriteLine("Done");
            //        }
            //    }






            //    Console.ForegroundColor = ConsoleColor.White;
            //    Console.BackgroundColor = ConsoleColor.Green;
            //    Console.WriteLine($"Archiving Local -> Remote");
            //    Console.ResetColor();

            //    foreach (var fi in dir.GetFiles("*.*", SearchOption.AllDirectories))
            //    {
            //        if (fi.Length >= minSize * 1024 * 1024 && !fi.Name.EndsWith(".arius"))
            //        {
            //            var relativeFileName = Path.GetRelativePath(dir.FullName, fi.FullName);

            //            Console.ForegroundColor = ConsoleColor.White;
            //            Console.BackgroundColor = ConsoleColor.Blue;
            //            Console.WriteLine($"File: {relativeFileName}");
            //            Console.ResetColor();

            //            //File is large enough AND not an .arius file

            //            Console.Write("Local file. Generating hash... ");
            //            var hash = FileUtils.GetHash(passphrase, fi.FullName);
            //            Console.WriteLine("Done");

            //            var sourceFullName = fi.FullName;
            //            var encryptedSourceFullName = Path.Combine(fi.DirectoryName, $"{fi.Name}.7z.arius");
            //            var contentBlobName = $"{hash}.7z.arius";
            //            var localPointerFullName = Path.Combine(fi.DirectoryName, $"{fi.Name}.arius");

            //            if (!_bu.Exists(contentBlobName))
            //            {
            //                //CREATE -- File does not exis on remote

            //                Console.WriteLine($"Archiving file: {fi.Length / 1024 / 1024} MB");

            //                Console.Write("Encrypting... ");
            //                _szu.EncryptFile(sourceFullName, encryptedSourceFullName, passphrase);
            //                Console.WriteLine("Done");

            //                Console.Write("Uploading... ");
            //                _bu.Archive(encryptedSourceFullName, contentBlobName, tier);
            //                Console.WriteLine("Done");

            //                Console.Write("Creating manifest...");
            //                var m = Manifest.CreateManifest(contentBlobName, relativeFileName);
            //                m.Archive(_bu, _szu, passphrase);
            //                Console.WriteLine("Done");
            //            }
            //            else
            //            {
            //                Console.Write("Binary exists on remote. Checking manifest... ");

            //                var m = Manifest.GetManifest(_bu, _szu, contentBlobName, passphrase); //TODO what if the manifest got deleted?

            //                if (!m.GetAllEntries(false, relativeFileName).Any())
            //                {
            //                    Console.Write("Adding reference to manifest... ");
            //                    m.AddEntry(relativeFileName);
            //                    m.Archive(_bu, _szu, passphrase);
            //                    Console.WriteLine("Done");
            //                }
            //                else
            //                {
            //                    Console.WriteLine("No changes");
            //                }
            //            }

            //            if (!File.Exists(localPointerFullName))
            //            {
            //                // File exists on remote, create pointer

            //                Console.Write("Creating local pointer... ");
            //                LocalAriusFile.CreatePointer(localPointerFullName, contentBlobName);
            //                Console.WriteLine("Done");
            //            }

            //            if (!keepLocal)
            //            {
            //                Console.Write("Deleting local file... ");
            //                File.Delete(sourceFullName);
            //                Console.WriteLine("Done");
            //            }

            //            File.Delete(encryptedSourceFullName);
            //        }

            //        // READ - n/a

            //        if (fi.Name.EndsWith(".arius"))
            //        {
            //            var relativeAriusFileName = Path.GetRelativePath(dir.FullName, fi.FullName);

            //            Console.ForegroundColor = ConsoleColor.White;
            //            Console.BackgroundColor = ConsoleColor.DarkBlue;
            //            Console.WriteLine($"File {relativeAriusFileName}");
            //            Console.ResetColor();

            //            Console.Write("Archived file. Checking manifest... ");
            //            var contentBlobName = File.ReadAllText(fi.FullName);
            //            var manifest = Manifest.GetManifest(_bu, _szu, contentBlobName, passphrase);


            //            var relativeFileName = LocalAriusFile.GetLocalContentName(relativeAriusFileName);
            //            if (!manifest.GetAllEntries(false, relativeFileName).Any())
            //            {
            //                // UPDATE The manifest does not have a pointer to this local file, ie the .arius file has been renamed

            //                Console.WriteLine("File has been renamed.");
            //                Console.Write("Updating manifest...");
            //                manifest.AddEntry(relativeFileName);
            //                manifest.Archive(_bu, _szu, passphrase);
            //                Console.WriteLine("Done");
            //            }
            //            else
            //            {
            //                Console.WriteLine("No changes");
            //            }
            //        }

            //        Console.WriteLine("");
            //    }

            //    Console.ForegroundColor = ConsoleColor.White;
            //    Console.BackgroundColor = ConsoleColor.Green;
            //    Console.WriteLine($"Synchronizing Remote with Local");
            //    Console.ResetColor();

            // ---------------- 3 ---------------------

            //    foreach (var contentBlobName in _bu.GetContentBlobNames())
            //    {
            //        var manifest = Manifest.GetManifest(_bu, _szu, contentBlobName, passphrase);
            //        var entries = manifest.GetLastEntries(true);

            //        bool toUpdate = false;

            //        foreach (var me in entries)
            //        {
            //            var localFile = Path.Combine(dir.FullName, me.RelativeFileName);

            //            if (!me.IsDeleted && !File.Exists(localFile) && !File.Exists($"{localFile}.arius"))
            //            {
            //                // DELETE - File is deleted
            //                manifest.AddEntry(me.RelativeFileName, true);
            //                toUpdate = true;

            //                Console.ForegroundColor = ConsoleColor.Red;
            //                Console.WriteLine($"File {me.RelativeFileName} is deleted. Marking as deleted on remote...");
            //                Console.ResetColor();
            //            }
            //        }

            //        if (toUpdate)
            //        {
            //            manifest.Archive(_bu, _szu, passphrase);
            //            Console.WriteLine("Done");
            //        }
            //    }

        }
    }
}
