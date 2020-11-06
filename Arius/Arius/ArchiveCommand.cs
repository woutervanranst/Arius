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

        /// <summary>
        /// Het gewone bestand met content erin
        /// </summary>
        class LocalContentFile
        {
            public LocalContentFile(DirectoryInfo root, FileInfo localContent)
            {
                _root = root;
                _localContent = localContent;
                _hash = new Lazy<string>(() => FileUtils.GetHash(_localContent.FullName));
            }
            private readonly DirectoryInfo _root;
            private readonly FileInfo _localContent;

            public EncryptedAriusContent AsAriusContentFile(bool dedup, string passphrase, DirectoryInfo root)
            {
                return EncryptedAriusContent.CreateEncryptedAriusContent(this, dedup, passphrase, root);
            }

            public AriusChunk[] GetChunks(bool dedup)
            {
                if (dedup)
                {
                    throw new NotImplementedException();

                    //var sb = new StreamBreaker();

                    //using var fs = new FileStream(_fi.FullName, FileMode.Open, FileAccess.Read);
                    //var chunks = sb.GetChunks(fs, fs.Length, SHA256.Create()).ToImmutableArray();
                    //fs.Position = 0;

                    //DirectoryInfo tempDir = new DirectoryInfo(Path.Combine(_fi.Directory.FullName, _fi.Name + ".arius"));
                    //tempDir.Create();

                    //foreach (var chunk in chunks)
                    //{
                    //    var chunkFullName = Path.Combine(tempDir.FullName, BitConverter.ToString(chunk.Hash));

                    //    byte[] buff = new byte[chunk.Length];
                    //    fs.Read(buff, 0, (int)chunk.Length);

                    //    using var fileStream = File.Create(chunkFullName);
                    //    fileStream.Write(buff, 0, (int)chunk.Length);
                    //    fileStream.Close();
                    //}

                    //fs.Close();

                    //var laf = new LocalAriusManifest(this);
                    //var lac = chunks.Select(c => new LocalAriusChunk("")).ToImmutableArray();

                    //var r = new AriusFile(this, laf, lac);

                    //return r;
                }
                else
                {
                    return new AriusChunk[] { new AriusChunk(_localContent, this.Hash) };
                }
            }

            public AriusManifest GetManifest(params EncryptedAriusChunk[] chunks) => AriusManifest.CreateManifest(this, chunks);

            //private AriusManifest CreateManifest(params EncryptedAriusChunk[] chunks) => AriusManifest.CreateManifest(this, chunks);

            public string Hash
            {
                get
                {
                    return _hash.Value;
                }
            }
            private Lazy<string> _hash;

            public string FullName => _localContent.FullName;
            public string RelativeName => Path.GetRelativePath(_root.FullName, FullName);
            public string AriusManifestFullName => $"{FullName}.manifest.arius";
            public DateTime CreationTimeUtc => _localContent.CreationTimeUtc;
            public DateTime LastWriteTimeUtc => _localContent.LastWriteTimeUtc;
        }

        /// <summary>
        /// Een Arius file met manifest en chunks
        /// </summary>
        class EncryptedAriusContent
        {
            public static EncryptedAriusContent CreateEncryptedAriusContent(LocalContentFile lcf, bool dedup, string passphrase, DirectoryInfo root) //AriusManifestFile amf, params EncryptedAriusChunk)
            {
                //DirectoryInfo tempDir = new DirectoryInfo(Path.Combine(_fi.Directory.FullName, _fi.Name + ".arius"));
                //tempDir.Create();

                var eacs = lcf
                    .GetChunks(dedup)
                    .AsParallel()
                        .WithDegreeOfParallelism(1)
                    .Select(ac => ac.AsEncryptedAriusChunk(passphrase))
                    .ToArray();

                var eamf = lcf
                    .GetManifest(eacs)
                    .GetAriusManifestFile(lcf.AriusManifestFullName)
                    .AsEncryptedAriusManifestFile(passphrase);


                return new EncryptedAriusContent(eamf, eacs);
            }

            
            public EncryptedAriusContent(EncryptedAriusManifestFile eamf, EncryptedAriusChunk[] eacs)
            {
                _eamf = eamf;
                _eacs = eacs;
            }
            private readonly EncryptedAriusManifestFile _eamf;
            private readonly EncryptedAriusChunk[] _eacs;

            public void Upload()
            {

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
        }

        /// <summary>
        /// Een bestand met .arius als extensie
        /// </summary>
        class AriusFile
        {
            public AriusFile(FileInfo fi)
            {
                //if (!fi.FullName.EndsWith(".arius"))
                //    throw new ArgumentException();

                _fi = fi;
            }
            private readonly FileInfo _fi;

            public string FullName => _fi.FullName;
            public string DirectoryName => _fi.DirectoryName;
            public string Name => _fi.Name;
        }

        /*
         * Conventie
         *  File.Name = de naam
         *  File.FullName = met full path
         * 
         * 
         */



        /// <summary>
        /// De Pointer
        /// </summary>
        class AriusManifestFile : AriusFile
        {
            //public static AriusManifestFile GetAriusManifest(LocalContentFile lcf, params EncryptedAriusChunk[] chunks)
            //{
            //    var m = AriusManifest.CreateManifest(lcf, chunks);
            //    var ariusManifestFullName = GetAriusManifestFullName(lcf);

            //    File.WriteAllText(ariusManifestFullName, m.AsJson);

            //    var fi = new FileInfo(ariusManifestFullName);
            //    return new AriusManifestFile(fi);
            //}
            public static AriusManifestFile GetAriusManifestFile(string ariusManifestFullName, AriusManifest ariusManifest)
            {
                var json = ariusManifest.AsJson();
                File.WriteAllText(ariusManifestFullName, json);

                var fi = new FileInfo(ariusManifestFullName);
                return new AriusManifestFile(fi);
            }

            private AriusManifestFile(FileInfo ariusManifestFile) : base(ariusManifestFile)
            {
            }

            public EncryptedAriusManifestFile AsEncryptedAriusManifestFile(string passphrase)
            {
                return EncryptedAriusManifestFile.GetEncryptedAriusManifestFile(this, passphrase);
            }
        }

        class EncryptedAriusManifestFile : AriusFile
        {
            public EncryptedAriusManifestFile(FileInfo file) : base(file) { }

            public static EncryptedAriusManifestFile GetEncryptedAriusManifestFile(AriusManifestFile ariusManifestFile, string passphrase)
            {
                var encryptedAriusChunkFullName = GetEncryptedAriusManifestFileFullName(ariusChunk);

                var szu = new SevenZipUtils();
                szu.EncryptFile(ariusChunk.FullName, encryptedAriusChunkFullName, passphrase);

                return new EncryptedAriusChunk(new FileInfo(encryptedAriusChunkFullName));
            }

            private static string GetEncryptedAriusManifestFileFullName(AriusChunk chunk) => $"{Path.Combine(chunk.DirectoryName, chunk.Hash)}.7z.arius";
        }

        class AriusManifest
        {
            public static AriusManifest CreateManifest(LocalContentFile lcf, params EncryptedAriusChunk[] chunks)
            {
                var me = new AriusManifestEntry
                {
                    RelativeName = lcf.RelativeName,
                    Version = DateTime.UtcNow,
                    IsDeleted = false,
                    EncryptedChunks = chunks.Select(c => c.Name),
                    CreationTimeUtc = lcf.CreationTimeUtc,
                    LastWriteTimeUtc = lcf.LastWriteTimeUtc,
                    Hash = lcf.Hash
                };

                return new AriusManifest
                {
                    Entries = new List<AriusManifestEntry>(new AriusManifestEntry[] { me })
                };
            }

            public List<AriusManifestEntry> Entries;

            public AriusManifestFile GetAriusManifestFile(string ariusManifestFullName) => AriusManifestFile.GetAriusManifestFile(ariusManifestFullName, this);

            public struct AriusManifestEntry
            {
                public string RelativeName { get; set; }
                public DateTime Version { get; set; }
                public bool IsDeleted { get; set; }
                public IEnumerable<string> EncryptedChunks { get; set; }
                public DateTime CreationTimeUtc { get; set; }
                public DateTime LastWriteTimeUtc { get; set; }
                public string Hash { get; set; }
            }

            public string AsJson() => JsonSerializer.Serialize(Entries, new JsonSerializerOptions { WriteIndented = true } ); // TODO waarom niet gewoon Serialize(this)
            public AriusManifest FromJson(string json) => JsonSerializer.Deserialize<AriusManifest>(json);
        }

        

        /// <summary>
        /// Binary chunk (NOT ENCRYPTED / ZIPPED)
        /// </summary>
        class AriusChunk : AriusFile
        {
            public AriusChunk(FileInfo file, string hash) : base(file)
            {
                //_hash = hash;
                Hash = hash;
            }
            //private readonly string _hash;
            public string Hash { get; private set; }


            public EncryptedAriusChunk AsEncryptedAriusChunk(string passphrase)
            {
                return EncryptedAriusChunk.GetEncryptedAriusChunk(this, passphrase);
            }
        }

        /// <summary>
        /// Encrypted + zipped binary chunk
        /// </summary>
        class EncryptedAriusChunk : AriusFile
        {
            public static EncryptedAriusChunk GetEncryptedAriusChunk(AriusChunk ariusChunk, string passphrase)
            {
                var encryptedAriusChunkFullName = GetEncryptedAriusChunkFullName(ariusChunk);

                var szu = new SevenZipUtils();
                szu.EncryptFile(ariusChunk.FullName, encryptedAriusChunkFullName, passphrase);

                return new EncryptedAriusChunk(new FileInfo(encryptedAriusChunkFullName));
            }

            private static string GetEncryptedAriusChunkFullName(AriusChunk chunk) => $"{Path.Combine(chunk.DirectoryName, chunk.Hash)}.7z.arius";

            //public override string FullName => ;


            private EncryptedAriusChunk(FileInfo encryptedAriusChunk) : base (encryptedAriusChunk) { }
        }
    }
}
