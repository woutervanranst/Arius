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
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

            /*
             * Test cases
             * Create File
             * Duplicate file
             * Rename file
             * Delete file
             * Add file again that was previously deleted
             * Rename content file
             * rename .arius file
             */
        }

        private int Execute(string passphrase, BlobUtils remoteBlobs, bool keepLocal, AccessTier tier, int minSize, bool simulate, DirectoryInfo dir)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.Green;
            Console.WriteLine($"Archiving Local -> Remote");
            Console.ResetColor();

            foreach (var fi in dir.GetFiles("*.*", SearchOption.AllDirectories))
            {
                var relativeFileName = Path.GetRelativePath(dir.FullName, fi.FullName);

                
                if (fi.Length >= minSize * 1024 * 1024 && !fi.Name.EndsWith(".arius"))
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.BackgroundColor = ConsoleColor.Blue;
                    Console.WriteLine($"File: {relativeFileName}");
                    Console.ResetColor();

                    //File is large enough AND not an .arius file

                    Console.Write("Local file. Generating hash... ");
                    var hash = FileUtils.GetHash(passphrase, fi.FullName);
                    Console.WriteLine("Done");

                    var sourceFullName = fi.FullName;
                    var encryptedSourceFullName = Path.Combine(fi.DirectoryName, $"{fi.Name}.7z.arius");
                    var blobName = $"{hash}.7z.arius";
                    var localPointerFullName = Path.Combine(fi.DirectoryName, $"{fi.Name}.arius");

                    if (!remoteBlobs.Exists(blobName))
                    {
                        //CREATE -- File does not exis on remote

                        Console.WriteLine($"Archiving file: {fi.Length / 1024 / 1024} MB");

                        Console.Write("Encrypting... ");
                        _szu.EncryptFile(sourceFullName, encryptedSourceFullName, passphrase);
                        Console.WriteLine("Done");

                        Console.Write("Uploading... ");
                        remoteBlobs.Upload(encryptedSourceFullName, blobName, tier);
                        Console.WriteLine("Done");

                        Console.Write("Creating manifest...");
                        AddManifestEntry(remoteBlobs, blobName, passphrase, relativeFileName);
                        Console.WriteLine("Done");
                    }
                    else
                    {
                        Console.Write("Binary exists on remote. Checking manifest... ");

                        var manifests = GetManifestsForBlob(remoteBlobs, blobName, passphrase); //TODO what if the manifest got deleted?

                        if (!manifests.Any(m => m.RelativeFileName == relativeFileName && !m.IsDeleted))
                        {
                            Console.Write("Adding reference to manifest... ");
                            //AddFileToManifestAndUpload(remoteBlobs, blobName, passphrase, manifest, relativeFileName);
                            AddManifestEntry(remoteBlobs, blobName, passphrase, relativeFileName, manifests: manifests);
                            Console.WriteLine("Done");
                        }
                        else
                        {
                            Console.WriteLine("No changes");
                        }
                    }

                    if (!File.Exists(localPointerFullName))
                    {
                        // File exists on remote, create pointer

                        Console.Write("Creating local pointer... ");
                        File.WriteAllText(localPointerFullName, blobName, Encoding.UTF8);
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

                //UPDATE - ie rename of an .arius file

                if (fi.Name.EndsWith(".arius"))
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.BackgroundColor = ConsoleColor.DarkBlue;
                    Console.WriteLine($"File {relativeFileName}");
                    Console.ResetColor();

                    Console.Write("Archived file. Checking manifest... ");
                    var blobName = File.ReadAllText(fi.FullName);
                    var manifests = GetManifestsForBlob(remoteBlobs, blobName, passphrase);

                    var originalFileName = GetContentFullName(relativeFileName); // Path.GetFileNameWithoutExtension(relativeFileName);
                    if (!manifests.Any(mm => mm.RelativeFileName == originalFileName))
                    {
                        // The manifest does not have a pointer to this local file, ie the .arius file has been renamed

                        Console.WriteLine("File has been renamed.");
                        Console.Write("Updating manifest...");
                        //AddFileToManifestAndUpload(remoteBlobs, blobName, passphrase, manifests, originalFileName);
                        AddManifestEntry(remoteBlobs, blobName, passphrase, originalFileName, manifests: manifests);
                        Console.WriteLine("Done");
                    }
                    else
                    {
                        Console.WriteLine("No changes");
                    }
                }
                
                Console.WriteLine("");
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.Green;
            Console.WriteLine($"Synchronizing Remote with Local");
            Console.ResetColor();


            foreach (var manifestBlobName in remoteBlobs.GetManifestBlobNames())
            {
                var manifests = GetManifests(remoteBlobs, passphrase, manifestBlobName);

                bool toUpdate = false;

                foreach (var manifest in manifests.Where(m => !m.IsDeleted).ToArray()) //ToArray as were modifying the collection along the way
                {
                    //TODO: Fout: als ik een file upload, dan rename, dan blijft de originele staan in de netry "isdeleted"
                    var localFile = Path.Combine(dir.FullName, manifest.RelativeFileName);
                    if (!File.Exists(localFile) && !File.Exists($"{localFile}.arius"))
                    {
                        // DELETE
                        var contentBlobName = GetContentBlobName(manifestBlobName);
                        AddManifestEntry(remoteBlobs, contentBlobName, passphrase, localFile, isDeleted: true, manifests: manifests, upload: false);
                        toUpdate = true;

                        Console.WriteLine("File");
                    }
                }

                if (toUpdate)
                {
                    var contentBlobName = GetContentBlobName(manifestBlobName);
                    UploadManifestsForBlob(remoteBlobs, contentBlobName, passphrase, manifests);
                }
            }

            return 0;
        }

        

        private void AddManifestEntry(BlobUtils remoteBlobs, string contentBlobName, string passphrase, string relativeFileName, DateTime? datetime = null, bool isDeleted = false, List<Manifest> manifests = null, bool upload = true)
        {
            if (manifests == null)
                AddManifestEntry(remoteBlobs, contentBlobName, passphrase, relativeFileName, datetime, isDeleted, new List<Manifest>(), upload);
            else
            {
                manifests.Add(new Manifest { RelativeFileName = relativeFileName, DateTime = DateTime.UtcNow, IsDeleted = isDeleted });

                if (upload)
                    UploadManifestsForBlob(remoteBlobs, contentBlobName, passphrase, manifests);
            }
        }


        //private Manifest GetNewManifest(string relativeFileName, DateTime? datetime = null, bool IsDeleted = false)
        //{
        //    return new Manifest { RelativeFileName = relativeFileName, DateTime = datetime ?? DateTime.UtcNow, IsDeleted = IsDeleted };
        //}

        //private void CreateAndUploadManifest(BlobUtils remoteBlobs, string contentBlobName, string passphrase, string relativeFileName)
        //{
        //    var manifest = new List<Manifest> { GetNewManifest(relativeFileName) };
        //    UploadManifestsForBlob(remoteBlobs, contentBlobName, passphrase, manifest);
        //}

        //private void AddFileToManifestAndUpload(BlobUtils remoteBlobs, string contentBlobName, string passphrase, List<Manifest> existingManifests, string relativeFileNameToAdd)
        //{
        //    AddFileToManifest(existingManifests, relativeFileNameToAdd);
        //    UploadManifestsForBlob(remoteBlobs, contentBlobName, passphrase, existingManifests);
        //}

        //private void AddFileToManifest(List<Manifest> existingManifests, string relativeFileNameToAdd)
        //{
        //    existingManifests.Add(GetNewManifest(relativeFileNameToAdd));
        //}

        private void UploadManifestsForBlob(BlobUtils remoteBlobs, string contentBlobName, string passphrase, List<Manifest> manifests)
        {
            var manifestName = GetManifestName(contentBlobName, false);
            var manifestFullName = Path.Combine(Path.GetTempPath(), manifestName);

            manifests = manifests.OrderBy(m => m.DateTime).ToList();
            var json = JsonSerializer.Serialize(manifests);
            File.WriteAllText(manifestFullName, json);

            var tempFileName1 = Path.GetTempFileName();

            _szu.EncryptFile(manifestFullName, tempFileName1, passphrase);
            File.Delete(manifestFullName);

            var manifestBlobName = GetManifestName(contentBlobName, true);
            remoteBlobs.Upload(tempFileName1, manifestBlobName, AccessTier.Cool);
            File.Delete(tempFileName1);
        }

        private List<Manifest> GetManifestsForBlob(BlobUtils remoteBlobs, string contentBlobName, string passphrase)
        {
            var manifestName = GetManifestName(contentBlobName, true);
            return GetManifests(remoteBlobs, passphrase, manifestName);
        }

        private List<Manifest> GetManifests(BlobUtils remoteBlobs, string passphrase, string manifestBlobName)
        {
            var tempFileName1 = Path.GetTempFileName();
            remoteBlobs.Download(manifestBlobName, tempFileName1);

            var tempFileName2 = Path.GetTempFileName();
            _szu.DecryptFile(tempFileName1, tempFileName2, passphrase);
            File.Delete(tempFileName1);

            var json = File.ReadAllText(tempFileName2);
            File.Delete(tempFileName2);

            var manifest = JsonSerializer.Deserialize<List<Manifest>>(json);

            return manifest;
        }

        private string GetManifestName(string contentBlobName, bool encrypted)
        {
            if (!encrypted)
                return $"{contentBlobName}.manifest";
            else
                return $"{contentBlobName}.manifest.7z.arius";
        }

        private string GetContentBlobName(string manifestBlobName)
        {
            //Ref https://stackoverflow.com/questions/5650909/regex-for-extracting-certain-part-of-a-string

            var match = Regex.Match(manifestBlobName, "^(?<contentBlobName>.*).manifest.7z.arius$");
            return match.Groups["contentBlobName"].Value;
        }

        private string GetContentFullName(string ariusFullName)
        {
            var match = Regex.Match(ariusFullName, "^(?<ariusFullName>.*).arius$");
            return match.Groups["ariusFullName"].Value;
        }

        class Manifest
        {
            public string RelativeFileName { get; set; }
            public DateTime DateTime { get; set; }
            public bool IsDeleted { get; set; }
        }

        
    }


    
}
