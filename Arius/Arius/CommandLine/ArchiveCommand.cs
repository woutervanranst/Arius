using Azure.Storage.Blobs.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.CommandLine;

namespace Arius.CommandLine
{
    internal class ArchiveCommand : IAriusCommand
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

        public Command GetCommand(ParsedCommandProvider pcp)
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

                string[] tiers = {"hot", "cool", "archive"};
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


            archiveCommand.Handler = CommandHandlerExtensions
                .Create<string, string, string, string, bool, string, int, bool, string>(
                    (accountName, accountKey, passphrase, container, keepLocal, tier, minSize, simulate, path) =>
                    {
                        pcp.CommandExecutorType = typeof(ArchiveCommandExecutor);

                        pcp.CommandExecutorOptions = new ArchiveOptions()
                        {
                            AccountName = accountName,
                            AccountKey = accountKey,
                            Passphrase = passphrase,
                            Container = container,
                            KeepLocal = keepLocal,
                            Tier = tier,
                            MinSize = minSize,
                            Simulate = simulate,
                            Path = path
                        };

                        return Task.FromResult<int>(0);
                    });

            return archiveCommand;
        }
    }

    internal struct ArchiveOptions : ILocalRootDirectoryOptions, ISHA256HasherOptions
    {
        public string AccountName { get; init; }
        public string AccountKey { get; init; }
        public string Passphrase { get; init; }
        public string Container { get; init; }
        public bool KeepLocal { get; init; }
        public string Tier { get; init; } 
        public int MinSize { get; init; }
        public bool Simulate { get; init; }
        public string Path { get; init; }
    }

    internal class ArchiveCommandExecutor  : ICommandExecutor
    {
        public ArchiveCommandExecutor(ICommandExecutorOptions options, LocalRootDirectory root)
        {
            var o = (ArchiveOptions)options;
            _root = root;
        }

        private readonly LocalRootDirectory _root;

        public int Execute()
        {
            var pointers = _root.Get<IPointerFile<IRemoteManifestBlob>>().GroupBy(c => c.GetObject().Hash, c => (ILocalFile)c);

            var content = _root.Get<ILocalContentFile>().GroupBy(c => c.Hash, c => (ILocalFile)c);

            var kka = pointers.Union(content).ToImmutableArray();

            return 0;
        }
        //public ArchiveCommandExecuter(AriusRootDirectory root, AriusRemoteArchive archive, string passphrase, bool keepLocal, AccessTier tier, int minSize, bool simulate, bool dedup)
        //{

        //}

    //private delegate int ArchiveDelegate(string accountName, string accountKey, string passphrase, string container, bool keepLocal, string tier, int minSize, bool simulate, string path);

        //private static int Execute(string accountName, string accountKey, string passphrase, string container, bool keepLocal, string tier, int minSize, bool simulate, string path)
        //{
        //    var accessTier = tier switch
        //    {
        //        "hot" => AccessTier.Hot,
        //        "cool" => AccessTier.Cool,
        //        "archive" => AccessTier.Archive,
        //        _ => throw new NotImplementedException()
        //    };

        //    var archive = new AriusRemoteArchive(accountName, accountKey, container);
        //    var root = new AriusRootDirectory(path);

        //    return Archive(root, archive, passphrase, keepLocal, accessTier, minSize, simulate, false);

        //}

        //public static int Archive(AriusRootDirectory root, AriusRemoteArchive archive, string passphrase, bool keepLocal, AccessTier tier, int minSize, bool simulate, bool dedup)
        //{

        //    ////TODO Simulate
        //    //// TODO MINSIZE
        //    ///
        //    /// TODO CHeck if the archive is deduped and password by checking the first amnifest file

        //    /*
        //     * 1. Ensure ALL LocalContentFiles (ie. all non-.arius files) are on the remote WITH a Manifest
        //     */

        //    //1.1 Ensure all chunks are uploaded
        //    var localContentPerHash = root
        //        .GetNonAriusFiles()
        //        .AsParallel()
        //        //.WithDegreeOfParallelism(1)
        //        .Select(fi => new LocalContentFile(root, fi, passphrase))
        //        .GroupBy(lcf => lcf.Hash)
        //        .ToImmutableArray();

        //    var remoteManifestHashes = archive
        //        .GetRemoteEncryptedAriusManifests()
        //        .Select(ream => ream.Hash)
        //        .ToImmutableArray();

        //    var localContentFilesToUpload = localContentPerHash
        //        .Where(g => !remoteManifestHashes.Contains(g.Key)) //TODO to Except
        //        .ToImmutableArray();

        //    var unencryptedChunksPerHash = localContentFilesToUpload
        //        .AsParallel()
        //        .WithDegreeOfParallelism(1) //moet dat hier staan om te paralleliseren of bij de GetChunks?
        //        .ToImmutableDictionary(
        //            g => g.Key,
        //            g => g.First().GetChunks(dedup));

        //    var remoteChunkHashes = archive
        //        .GetRemoteEncryptedAriusChunks()
        //        .Select(reac => reac.Hash)
        //        .ToImmutableArray();

        //    var encryptedChunksToUploadPerHash = unencryptedChunksPerHash
        //        .AsParallel()
        //        .WithDegreeOfParallelism(1)
        //        .ToImmutableDictionary(
        //            p => p.Key,
        //            p => p.Value
        //                .Where(uec => !remoteChunkHashes.Contains(uec.Hash)) //TODO met Except
        //                .Select(c => c.GetEncryptedAriusChunk(passphrase)).ToImmutableArray()
        //        );

        //    var encryptedChunksToUpload = encryptedChunksToUploadPerHash.Values
        //        .SelectMany(eac => eac)
        //        .ToImmutableArray();


        //    //Upload Chunks
        //    archive.Upload(encryptedChunksToUpload, tier);

        //    //Delete Chunks (niet enkel de uploaded ones maar ook de generated ones)
        //    foreach (var encryptedChunkFullName in encryptedChunksToUpload
        //        .Select(uec => uec.FullName)
        //        .Distinct())
        //        File.Delete(encryptedChunkFullName);

        //    //1.2 Create manifests for NEW Content (as they do not exist) - this does not yet include the references to the pointers
        //    var createdManifestsPerHash = localContentFilesToUpload
        //        .AsParallel()
        //        .WithDegreeOfParallelism(1)
        //        .Select(g => RemoteEncryptedAriusManifest.Create(
        //            g.First().Hash,
        //            unencryptedChunksPerHash[g.Key]
        //                .Select(uec => archive.GetRemoteEncryptedAriusChunk(uec.Hash)),
        //            archive, passphrase))
        //        .ToDictionary(
        //            ream => ream.Hash,
        //            ream => ream);

        //    /*
        //     * 2. Ensure Pointers exist/are create for ALL LocalContentFiles
        //     */
        //    localContentPerHash
        //        .AsParallel()
        //        .WithDegreeOfParallelism(1)
        //        .SelectMany(g => g)
        //        .Where(lcf => !lcf.AriusPointerFileInfo.Exists)
        //        .ForAll(lcf =>
        //        {
        //            var manifest = createdManifestsPerHash.ContainsKey(lcf.Hash) ?
        //                createdManifestsPerHash[lcf.Hash] :
        //                archive.GetRemoteEncryptedAriusManifestByHash(lcf.Hash);

        //            AriusPointerFile.Create(root, lcf, manifest);
        //        });

        //    /*
        //     * 3. Synchronize ALL MANIFESTS with the local file system
        //     */

        //    var ariusPointersPerManifestName = root.GetAriusPointerFiles()
        //        .GroupBy(apf => apf.EncryptedManifestName)
        //        .ToImmutableDictionary(
        //            g => g.Key,
        //            g => g.ToList());

        //    // TODO QUID BROKEN POINTERFILES

        //    //TODO met AZCOPY
        //    archive.GetRemoteEncryptedAriusManifests()
        //        .AsParallel()
        //        //.WithDegreeOfParallelism(1)
        //        .ForAll(a =>
        //        {
        //            a.Update(ariusPointersPerManifestName[a.Name], passphrase);
        //        });

        //    /*
        //     * 4. Remove LocalContentFiles
        //     */
        //    if (!keepLocal)
        //        root.GetNonAriusFiles().AsParallel().ForAll(fi => fi.Delete());

        //    return 0;
        //}
    }
}
