using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

        private int Execute(string passphrase, BlobUtils remoteBlobs, bool keepLocal, AccessTier tier, int minSize, bool simulate, DirectoryInfo dir)
        {
            foreach (var fi in dir.GetFiles("*.*", SearchOption.AllDirectories))
            {
                var relativeFileName = Path.GetRelativePath(dir.FullName, fi.FullName);

                Console.ForegroundColor = ConsoleColor.White;
                Console.BackgroundColor = ConsoleColor.Blue;
                Console.WriteLine($"File {relativeFileName}");
                Console.ResetColor();

                if (fi.Length >= minSize * 1024 * 1024 && !fi.Name.EndsWith(".arius"))
                {
                    //File is large enough AND not an .arius file

                    Console.Write("Generating hash... ");
                    var hash = FileUtils.GetHash(passphrase, fi.FullName);
                    Console.WriteLine("Done");

                    var sourceFullName = fi.FullName;
                    var encryptedSourceFullName = Path.Combine(fi.DirectoryName, $"{fi.Name}.7z.arius");
                    var blobTargetName = $"{hash}.7z.arius";
                    var manifestFullName = Path.Combine(fi.DirectoryName, $"{blobTargetName}.manifest");
                    var manifest7zFullName = $"{manifestFullName}.7z.arius";
                    var blobTargetManifestFullName = (new FileInfo(manifest7zFullName)).Name;
                    var localTargetFullName = Path.Combine(fi.DirectoryName, $"{fi.Name}.arius");

                    if (!remoteBlobs.Exists(blobTargetName))
                    {
                        // File does not exis on remote

                        //CREATE
                        
                        Console.WriteLine($"Archiving file: {fi.Length / 1024 / 1024} MB");

                        Console.Write("Encrypting... ");
                        _szu.EncryptFile(sourceFullName, encryptedSourceFullName, passphrase);
                        Console.WriteLine("Done");

                        Console.Write("Uploading... ");
                        remoteBlobs.Upload(encryptedSourceFullName, blobTargetName, tier);
                        Console.WriteLine("Done");

                        Console.Write("Creating manifest...");
                        CreateManifest(passphrase, remoteBlobs, relativeFileName, manifestFullName, manifest7zFullName, blobTargetManifestFullName);
                        Console.WriteLine("Done");
                    }
                    else
                    {
                        Console.Write("File already exists on remote. Checking manifest...");

                        var m = GetManifest(passphrase, remoteBlobs, blobTargetManifestFullName);

                    }



                    


                    if (!File.Exists(localTargetFullName))
                    {
                        // File exists on remote, create pointer

                        Console.Write("Creating local pointer... ");
                        File.WriteAllText(localTargetFullName, blobTargetName, Encoding.UTF8);
                        Console.WriteLine("Done");
                    }

                    if (!keepLocal)
                    {
                        Console.Write("Deleting local file... ");
                        File.Delete(sourceFullName);
                        Console.WriteLine("Done");
                    }

                    File.Delete(encryptedSourceFullName);
                }
                

                // READ - n/a

                // UPDATE

                // DELETE


                //if (fi.Length < minSize * 1024 * 1024)
                //    continue;

                //if (fi.Name.EndsWith(".arius"))
                //    continue;


                


                Console.WriteLine("");
            }

            return 0;
        }

        private void CreateManifest(string passphrase, BlobUtils remoteBlobs, string relativeFileName, string manifestFullName, string manifest7zFullName, string blobTargetManifestFullName)
        {
            var manifest = new List<Manifest> { new Manifest { RelativeName = relativeFileName, DateTime = DateTime.UtcNow, IsDeleted = false } };
            var json = JsonSerializer.Serialize(manifest);
            File.WriteAllText(manifestFullName, json);
            _szu.EncryptFile(manifestFullName, manifest7zFullName, passphrase);
            File.Delete(manifestFullName);

            remoteBlobs.Upload(manifest7zFullName, blobTargetManifestFullName, AccessTier.Cool);
            File.Delete(manifest7zFullName);
        }

        private List<Manifest> GetManifest(string passphrase, BlobUtils remoteBlobs, string manifestBlobName)
        {
            var tempFileName1 = Path.GetTempFileName();
            var tempFileName2 = Path.GetTempFileName();

            remoteBlobs.Download(manifestBlobName, tempFileName1);
            _szu.DecryptFile(tempFileName1, tempFileName2, passphrase);
            File.Delete(tempFileName1);
            var json = File.ReadAllText(tempFileName2);
            File.Delete(tempFileName2);

            var manifest = JsonSerializer.Deserialize<List<Manifest>>(json);

            return manifest;
        }


        class Manifest
        {
            public string RelativeName { get; set; }
            public DateTime DateTime { get; set; }
            public bool IsDeleted { get; set; }
        }

        
    }


    
}
