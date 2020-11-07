using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using SevenZip;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Arius.StreamBreaker;

namespace Arius
{
    class ArchiveCommand
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

        private static int Execute(string accountName, string accountKey, string passphrase, string container, bool keepLocal, string tier, int minSize, bool simulate, string path)
        {
            //var bu = new BlobUtils(accountName, accountKey, container);

            var root = new DirectoryInfo(path);

            var accessTier = tier switch
            {
                "hot" => AccessTier.Hot,
                "cool" => AccessTier.Cool,
                "archive" => AccessTier.Archive,
                _ => throw new NotImplementedException()
            };

            //var szu = new SevenZipUtils();

            var ac = new ArchiveCommand();
            return ac.Execute(passphrase, keepLocal, accessTier, minSize, simulate, root);

            ////TODO KeepLocal
            //// TODO Simulate

            ///*
            // * Test cases
            // * Create File
            // * Duplicate file
            // * Rename file
            // * Delete file
            // * Add file again that was previously deleted
            // * Rename content file
            // * rename .arius file
            // */
        }

        public ArchiveCommand()
        {
        }

        private int Execute(string passphrase, bool keepLocal, AccessTier tier, int minSize, bool simulate, DirectoryInfo root)
        {
            root.GetFiles("*.*", SearchOption.AllDirectories)
                .AsParallel()
                .WithDegreeOfParallelism(1)
                .Where(fi => !fi.Name.EndsWith(".arius"))
                .Select(fi => new LocalContentFile(root, fi))
                .Select(f => f.AsAriusContentFile(false, passphrase, root))
                .ForAll(eac => eac.Upload());





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
            //                _bu.Upload(encryptedSourceFullName, contentBlobName, tier);
            //                Console.WriteLine("Done");

            //                Console.Write("Creating manifest...");
            //                var m = Manifest.CreateManifest(contentBlobName, relativeFileName);
            //                m.Upload(_bu, _szu, passphrase);
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
            //                    m.Upload(_bu, _szu, passphrase);
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
            //                manifest.Upload(_bu, _szu, passphrase);
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
            //            manifest.Upload(_bu, _szu, passphrase);
            //            Console.WriteLine("Done");
            //        }
            //    }

            return 0;
        }
    }
}
