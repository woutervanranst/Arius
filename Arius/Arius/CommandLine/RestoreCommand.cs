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

            var syncOption = new Option<bool>("--synchronize",
                "Create pointers on local for every remote file, without actually downloading the files");
            restoreCommand.AddOption(syncOption);

            var downloadOption = new Option<bool>("--download",
                "Download file files for the given pointer in <path> (file) or all the pointers in <path> (folder)");
            restoreCommand.AddOption(downloadOption);

            var keepPointersOption = new Option<bool>("--keep-pointers",
                "Keep pointer files after downloading content files");
            restoreCommand.AddOption(keepPointersOption);

            var pathArgument = new Argument<string>("path",
                getDefaultValue: () => Environment.CurrentDirectory,
                "Path to restore. Default: current directory");
            restoreCommand.AddArgument(pathArgument);

            restoreCommand.Handler = CommandHandlerExtensions.Create<string, string, string, string, bool, bool, bool, string>((accountName, accountKey, passphrase, container, synchronize, download, keepPointers, path) =>
            {
                pcp.CommandExecutorType = typeof(RestoreCommandExecutor);

                pcp.CommandExecutorOptions = new RestoreOptions
                {
                    AccountName = accountName,
                    AccountKey = accountKey,
                    Passphrase = passphrase,
                    Container = container,
                    Synchronize = synchronize,
                    Download = download,
                    KeepPointers = keepPointers,
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
        public bool Synchronize { get; init; }
        public bool Download { get; init; }
        public bool KeepPointers { get; init; }
        public string Path { get; init; }

        public bool Dedup => false;
        public AccessTier Tier { get => throw new NotImplementedException(); init => throw new NotImplementedException(); } // Should not be used
        public bool KeepLocal { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }
        public int MinSize { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }
        public bool Simulate { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }
    }

    internal class RestoreCommandExecutor : ICommandExecutor
    {
        private readonly RestoreOptions _options;
        private readonly ILogger<ArchiveCommandExecutor> _logger;
        private readonly LocalRootRepository _localRoot;
        //private readonly AriusRepository _remoteArchive;
        private readonly LocalManifestFileRepository _manifestRepository;
        private readonly RemoteEncryptedChunkRepository _chunkRepository;
        private readonly ManifestService _manifestService;
        private readonly PointerService _pointerService;
        private readonly IEncrypter _encrypter;

        public RestoreCommandExecutor(ICommandExecutorOptions options,
            ILogger<ArchiveCommandExecutor> logger,
            LocalRootRepository localRoot,
            //AriusRepository remoteArchive,
            LocalManifestFileRepository manifestRepository,
            RemoteEncryptedChunkRepository chunkRepository,
            ManifestService manifestService,
            PointerService pointerService,
            IEncrypter encrypter)
        {
            _options = (RestoreOptions) options;
            _logger = logger;
            _localRoot = localRoot;
            //_remoteArchive = remoteArchive;
            _manifestRepository = manifestRepository;
            _chunkRepository = chunkRepository;
            _manifestService = manifestService;
            _pointerService = pointerService;
            _encrypter = encrypter;
        }

        public int Execute()
        {
            if (_localRoot.Exists)
            {
                if (!_localRoot.IsEmpty)
                {
                    // use !pf.LocalContentFileInfo.Exists 
                    _logger.LogWarning("The folder is not empty. There may be lingering files after the restore.");
                    //TODO LOG WARNING if local root directory contains other things than the pointers with their respecitve localcontentfiles --> will not be overwritten but may be backed up
                }

                if (_options.Synchronize)
                    Synchronize();

                if (_options.Download)
                    Download();
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
            var relativeNamesThatShouldExist = pointerEntriesperManifest.Values
                .SelectMany(x => x)
                .Select(x => x.RelativeName); //root.GetFullName(x));

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

        private void Download()
        {
            var pointerFiles = _localRoot.GetAll().OfType<IPointerFile>().ToImmutableArray();

            var pointerFilesPerManifest = pointerFiles
                .AsParallelWithParallelism()
                .Where(pf => !pf.LocalContentFileInfo.Exists) //TODO test dit + same hash?
                .GroupBy(pf => pf.ManifestFileName)
                .ToImmutableDictionary(
                    g =>
                    {
                        var hashValue = new HashValue {Value = g.Key};
                        var manifestFile = _manifestRepository.GetById(hashValue);
                        return _manifestService.ReadManifestFile(manifestFile);
                    },
                    g => g.ToList());

            //TODO QUID FILES THAT ALREADY EXIST / WITH DIFFERNT HASH?

            var chunksToDownload = pointerFilesPerManifest.Keys
                .AsParallelWithParallelism()
                .SelectMany(mf => mf.ChunkNames)
                .Distinct()
                .Select(chunkName => _chunkRepository.GetById(chunkName))
                .ToImmutableArray();

            var encryptedChunks = _chunkRepository.DownloadAll(chunksToDownload);

            var unencryptedChunks = encryptedChunks
                .AsParallelWithParallelism()
                .Select(ec => (IChunkFile)_encrypter.Decrypt(ec, true))
                .ToImmutableDictionary(
                    c => c.Hash,
                    c => c);

            var pointersWithChunks = pointerFilesPerManifest.Keys
                .GroupBy(mf => new HashValue {Value = mf.Hash})
                .Select(
                    g => new
                    {
                        PointerFileEntry = g.SelectMany(m => m.PointerFileEntries).ToImmutableArray(),
                        UnencryptedChunks = g.SelectMany(m => m.ChunkNames.Select(ecn => unencryptedChunks[g.Key])).ToImmutableArray()
                    })
                .ToImmutableArray();

            pointersWithChunks
                .AsParallelWithParallelism()
                .ForAll(p => Restore(_localRoot, p.PointerFileEntry, p.UnencryptedChunks));

            if (!_options.KeepPointers)
                pointerFiles.AsParallel().ForAll(apf => apf.Delete());
        }
        private void Restore(LocalRootRepository root, ImmutableArray<Manifest.PointerFileEntry> pfes, ImmutableArray<IChunkFile> chunks)
        {
            if (chunks.Length == 1)
            {
                //No dedup
                var chunk = chunks.Single();
                var chunkFileInfo = new FileInfo(chunk.FullName);

                for (int i = 0; i < pfes.Length; i++)
                {
                    var pfe = pfes[i];
                    var targetFileInfo = _localRoot.GetLocalContentFileInfo(pfe);

                    if (i == 0)
                        chunkFileInfo.MoveTo(targetFileInfo.FullName);
                    else
                        chunkFileInfo.CopyTo(targetFileInfo.FullName);

                    targetFileInfo.CreationTimeUtc = pfe.CreationTimeUtc!.Value;
                    targetFileInfo.LastWriteTimeUtc = pfe.LastWriteTimeUtc!.Value;
                }
            }
            else
            {
                throw new NotImplementedException();
            }
        }
    }
}










