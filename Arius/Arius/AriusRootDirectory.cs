using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Azure.Storage.Blobs.Models;
using MoreLinq;

namespace Arius
{
    class AriusRootDirectory
    {
        public AriusRootDirectory(string path)
        {
            _root = new DirectoryInfo(path);
        }

        private readonly DirectoryInfo _root;

        public string FullName => _root.FullName;

        public int Archive(AriusRemoteArchive archive, string passphrase, bool keepLocal, AccessTier tier, int minSize, bool simulate)
        {
                
            ////TODO KeepLocal
            //// TODO Simulate

            /* 1. LocalContentFiles (ie. all non-.arius files)
             *  READ > N/A
             *  CREATE > uploaden en manifest schrijven
             *  UPDATE > uploaden en manifest overschrijven
             *  DELETE > N/A
             */

            //1.1 Ensure all binaries are uploaded
            var localContentPerHash = _root
                    .GetFiles("*.*", SearchOption.AllDirectories)
                    .AsParallel()
                        .WithDegreeOfParallelism(1)
                    .Where(fi => !fi.Name.EndsWith(".arius"))
                    .Select(fi => new LocalContentFile(this, fi))
                    .GroupBy(lcf => lcf.Hash)
                    .ToImmutableArray();


            var remoteManifests = archive
                .GetEncryptedManifestFileBlobItems()
                .Select(bi => RemoteEncryptedAriusManifestFile.Create(bi))
                .ToImmutableArray();
            var remoteContentHashes = remoteManifests
                .Select(s => s.Hash)
                .ToImmutableArray();
            var localContentFilesToUpload = localContentPerHash
                .Where(g => !remoteContentHashes.Contains(g.Key))
                .Select(g => g.First())
                .ToImmutableArray();
            var encryptedAriusContentsToUpload = localContentFilesToUpload
                .Select(lcf => lcf.CreateEncryptedAriusContent(false, passphrase))
                .ToImmutableArray();
            var chunksToUpload = encryptedAriusContentsToUpload
                .SelectMany(eac => eac.EncryptedAriusChunks)
                // . EXCEPT ALREADY REMOTE TODO archive.GetEncryptedAriusChunkBlobItems
                .DistinctBy(eac => eac.UnencryptedHash)
                .ToImmutableArray();

            archive.Archive(chunksToUpload, tier);

            //localContentPerHash
            //    .AsParallel()
            //        .WithDegreeOfParallelism(1)
            //    .ForAll(eac => eac.Archive(archive, tier));

            /* 2. Local AriusPointerFiles (.arius files of LocalContentFiles that were not touched in #1) --- de OVERBLIJVENDE .arius files
             * CREATE >N/A
             * READ > N/A
             * UPDATE > remote manifest bijwerken (naming, plaats, ;;;)
             * DELETE > remote manifest bijwerken
             */

            //var remainingAriusPointers = _root
            //    .GetFiles("*.arius", SearchOption.AllDirectories)
            //    .Select(fi => AriusPointerFile.Create(fi))
            //    .Except(localContentPerHash.Select(eac => eac.PointerFile))
            //    .ToImmutableArray();

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

            return 0;
        }
    }
}
