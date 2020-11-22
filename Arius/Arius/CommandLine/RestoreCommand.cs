using System;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;
using Arius.Services;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace Arius.CommandLine
{
    internal class RestoreCommand : IAriusCommand
    {
        /*
         * arius restore
               --accountname <accountname> 
               --accountkey <accountkey> 
               --passphrase <passphrase>
              (--download)
         */

        Command IAriusCommand.GetCommand(ParsedCommandProvider pcp)
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

            restoreCommand.Handler = CommandHandler.Create<string, string, string, string, bool, string>((accountName, accountKey, passphrase, container, download, path) =>
            {
                pcp.CommandExecutorType = typeof(RestoreCommandExecutor);

                pcp.CommandExecutorOptions = new RestoreOptions
                {
                    AccountName = accountName,
                    AccountKey = accountKey,
                    Passphrase = passphrase,
                    Container = container,
                    Download = download,
                    Path = path
                };

                return Task.FromResult<int>(0);
            });

            return restoreCommand;
        }
    }

    internal struct RestoreOptions : ICommandExecutorOptions,
        ILocalRootDirectoryOptions,
        IChunkerOptions,
        ISHA256HasherOptions,
        IAzCopyUploaderOptions,
        IEncrypterOptions,
        IRemoteChunkRepositoryOptions,
        IAriusRepositoryOptions
    {
        public string AccountName { get; init; }
        public string AccountKey { get; init; }
        public string Passphrase { get; init; }
        public string Container { get; init; }
        public bool Download { get; init; }
        public string Path { get; init; }

        public bool Dedup => false;
        public AccessTier Tier { get => throw new NotImplementedException(); init => throw new NotImplementedException(); } // Should not be used
        public bool KeepLocal { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }
        public int MinSize { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }
        public bool Simulate { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }
    }

    internal class RestoreCommandExecutor : ICommandExecutor
    {
        private readonly ICommandExecutorOptions _options;
        private readonly ILogger<ArchiveCommandExecutor> _logger;
        private readonly LocalRootRepository _localRoot;
        private readonly AriusRepository _remoteArchive;
        private readonly LocalManifestFileRepository _manifestRepository;
        private readonly ManifestService _manifestService;
        private readonly PointerService _pointerService;

        public RestoreCommandExecutor(ICommandExecutorOptions options,
            ILogger<ArchiveCommandExecutor> logger,
            LocalRootRepository localRoot,
            AriusRepository remoteArchive,
            LocalManifestFileRepository manifestRepository,
            ManifestService manifestService,
            PointerService pointerService)
        {
            _options = options;
            _logger = logger;
            _localRoot = localRoot;
            _remoteArchive = remoteArchive;
            _manifestRepository = manifestRepository;
            _manifestService = manifestService;
            _pointerService = pointerService;
        }

        public int Execute()
        {
            if (_localRoot.Exists)
            {
                //TODO LOG WARNING if local root directory contains other things than the pointers with their respecitve localcontentfiles --> will not be overwritten but may be backed up

                Synchronize();

                //if (download)
                //    Download(archive, root, passphrase);
            }
            //else if (File.Exists(path) && path.EndsWith(".arius"))
            //{
            //    // Restore one file

            //}
            else
            {
                throw new NotImplementedException();
            }
            return 0;
        }

        private void Synchronize()
        {
            //Synchronize the local root to the remote repository
            var manifestFiles = _manifestRepository.GetAll();

            var pointerEntriesperManifest = manifestFiles.AsParallelWithParallelism()
                //.Select(mf => _manifestService.ReadManifestFile(mf))
                //.ToImmutableDictionary(
                //    m => m,
                //    m => m.GetLastExistingEntriesPerRelativeName().ToImmutableArray()
                .ToImmutableDictionary(
                    mf => mf,
                    mf => _manifestService.ReadManifestFile(mf).GetLastExistingEntriesPerRelativeName().ToImmutableArray()
                );

            _logger.LogInformation($"{pointerEntriesperManifest.Values.Sum(pfes => pfes.Length)} files in latest version of remote");


            _logger.LogInformation($"Synchronizing state of local folder with remote... ");

            //1. POINTERS THAT EXIST REMOTE BUT NOT LOCAL --> TO BE CREATED
            var createdPointers = pointerEntriesperManifest
                .AsParallelWithParallelism()
                .SelectMany(p => p.Value
                    .Where(pfe => !_localRoot.GetPointerFileInfo(pfe).Exists)
                    .Select(pfe =>
                    {
                        var apf = _pointerService.CreatePointerFile(_localRoot, pfe, p.Key);
                        _logger.LogInformation($"Pointer '{apf.RelativeName}' created");

                        return apf;
                    }))
                .ToImmutableArray();

            // 2. POINTERS THAT EXIST LOCAL BUT NOT REMOTE --> TO BE DELETED
            var relativeNamesThatShouldExist = pointerEntriesperManifest.Values.SelectMany(x => x).Select(x => x.RelativeName); //root.GetFullName(x));

            _localRoot.GetAll().OfType<IPointerFile>()
                .AsParallelWithParallelism()
                .Where(pf => !relativeNamesThatShouldExist.Contains(pf.RelativeName))
                .ForAll(pf =>
                {
                    pf.Delete();

                    Console.WriteLine($"Pointer for '{pf.RelativeName}' deleted");
                });

            _localRoot.DeleteEmptySubdirectories();
        }
    }


    //private static int Synchronize(AriusRemoteArchive archive, AriusRootDirectory root, string passphrase)
    //{









    //    /*
    //     * Test cases
    //     *      empty dir
    //     *      dir with files > not to be touched?
    //     *      dir with pointers - too many pointers > to be deleted
    //     *      dir with pointers > not enough pointers > to be synchronzed
    //     *      remote with isdeleted and local present > should be deleted
    //     *      remote with !isdeleted and local not present > should be created
    //     *      also in subdirectories
    //     *      in ariusfile : de verschillende extensions
    //     *      files met duplicates enz upload download
    //     *      al 1 file lokaal > kopieert de rest
    //     *      restore > normal binary file remains untouched
    //     * directory more than 2 deep without other files
    //     *  download > local files exist s> don't download all
            // * restore naar directory waar al andere bestanden (binaries) instaan -< are not touched (dan moet ge maa rnaar ne lege restoren)
    //     * */


    //    return 0;
    //}

    //private static int Download(AriusRemoteArchive archive, AriusRootDirectory root, string passphrase)
    //{
    //    var pointerFilesPerRemoteEncryptedManifest = root.GetAriusPointerFiles()
    //        .AsParallel()
    //        .Where(apf => !File.Exists(apf.LocalContentFileFullName)) //TODO test dit
    //        .GroupBy(apf => apf.EncryptedManifestName)
    //        .ToImmutableDictionary(
    //            apf => archive.GetRemoteEncryptedAriusManifestByBlobItemName(apf.Key),
    //            apf => apf.ToList());

    //TODO QUID FILES THAT ALREADY EXIST / WITH DIFFERNT HASH?

    //    var chunkNamesToDownload = pointerFilesPerRemoteEncryptedManifest.Keys
    //        .AsParallel()
    //        .SelectMany(ream => ream.GetEncryptedChunkNames(passphrase))
    //        .Distinct()
    //        .ToImmutableArray();

    //    var downloadDirectory = new DirectoryInfo(Path.Combine(root.FullName, ".ariusdownload"));
    //    if (downloadDirectory.Exists)
    //        downloadDirectory.Delete(true);
    //    downloadDirectory.Create();
    //    archive.Download(chunkNamesToDownload, downloadDirectory);

    //    var unencryptedChunks = downloadDirectory.GetFiles()
    //        .AsParallel()
    //        .Select(fi =>
    //        {
    //            var szu = new SevenZipUtils();

    //            var extractedFile = fi.FullName.TrimEnd(".7z.arius");
    //            szu.DecryptFile(fi.FullName, extractedFile, passphrase);
    //            fi.Delete();

    //            return new FileInfo(extractedFile);
    //        })
    //        .ToImmutableDictionary(x => x.Name, x => x);

    //    var pointersWithChunksPerHash = pointerFilesPerRemoteEncryptedManifest.Keys
    //        .GroupBy(ream => ream.Hash)
    //        .ToDictionary(
    //            g => g.Key,
    //            g => new
    //            {
    //                PointerFileEntry = g.SelectMany(ream => ream.GetAriusPointerFileEntries(passphrase)).ToImmutableArray(),
    //                UnencryptedChunks = g.SelectMany(ream => ream.GetEncryptedChunkNames(passphrase).Select(ecn => unencryptedChunks[ecn.TrimEnd(".7z.arius")])).ToImmutableArray()
    //            });

    //    pointersWithChunksPerHash
    //        .AsParallel()
    //        .ForAll(p =>
    //        {
    //            Restore(root, p.Value.PointerFileEntry, p.Value.UnencryptedChunks);
    //        });

    //    downloadDirectory.Delete();
    //    root.GetAriusPointerFiles().AsParallel().ForAll(apf => apf.Delete());

    //    return 0;
    //}

    //public static void Restore(AriusRootDirectory root, ImmutableArray<RemoteEncryptedAriusManifest.AriusManifest.AriusPointerFileEntry> apfes, ImmutableArray<FileInfo> chunks)
    //{
    //    if (chunks.Length == 1)
    //    {
    //        //No dedup
    //        var chunk = chunks.Single();

    //        chunk.MoveTo(root.GetFullName(apfes.First()));

    //        chunk.CreationTimeUtc = apfes.First().CreationTimeUtc!.Value;
    //        chunk.LastWriteTimeUtc = apfes.First().LastWriteTimeUtc!.Value;

    //        apfes.Skip(1)
    //            .AsParallel()
    //            .ForAll(apfe =>
    //            {
    //                var copy = chunk.CopyTo(root.GetFullName(apfe), true);

    //                copy.CreationTimeUtc = apfe.CreationTimeUtc!.Value;
    //                copy.LastWriteTimeUtc = apfe.LastWriteTimeUtc!.Value;
    //            });
    //    }
    //    else
    //    {


    //    }

    //}

}