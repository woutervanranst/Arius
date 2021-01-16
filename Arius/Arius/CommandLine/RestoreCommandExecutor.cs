using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;
using Arius.Services;
using Microsoft.Extensions.Logging;

namespace Arius.CommandLine
{
    internal class RestoreCommandExecutor : ICommandExecutor
    {
        private readonly RestoreOptions _options;
        private readonly ILogger<RestoreCommandExecutor> _logger;

        private readonly AzureRepository _repo;

        //private readonly LocalRootRepository _localRoot;
        ////private readonly AriusRepository _remoteArchive;
        //private readonly LocalManifestFileRepository _manifestRepository;
        //private readonly RemoteEncryptedChunkRepository _chunkRepository;
        //private readonly ManifestService _manifestService;
        //private readonly PointerService _pointerService;
        //private readonly IEncrypter _encrypter;
        private readonly DirectoryInfo _root;

        public RestoreCommandExecutor(ICommandExecutorOptions options,
                ILogger<RestoreCommandExecutor> logger,


                AzureRepository repo
            )
            //,
            //    LocalRootRepository localRoot,
            //    //AriusRepository remoteArchive,
            //    LocalManifestFileRepository manifestRepository,
            //    RemoteEncryptedChunkRepository chunkRepository,
            //    ManifestService manifestService,
            //    PointerService pointerService,
            //    IEncrypter encrypter)
        {
            _options = (RestoreOptions) options;
            _logger = logger;
            _root = new DirectoryInfo(_options.Path);
            _repo = repo;
            //    _localRoot = localRoot;
            //    //_remoteArchive = remoteArchive;
            //    _manifestRepository = manifestRepository;
            //    _chunkRepository = chunkRepository;
            //    _manifestService = manifestService;
            //    _pointerService = pointerService;
            //    _encrypter = encrypter;
        }

        public int Execute()
        {
            if (_root.Exists)
            {
                if (!_root.EnumerateFiles().Any())
                {
                    // use !pf.LocalContentFileInfo.Exists 
                    _logger.LogWarning("The folder is not empty. There may be lingering files after the restore.");
                    //TODO LOG WARNING if local root directory contains other things than the pointers with their respecitve localcontentfiles --> will not be overwritten but may be backed up
                }
            }

            //if (_options.Synchronize)
            //    Synchronize();

            //    if (_options.Download)
            //        Download();
            //}
            ////else if (File.Exists(path) && path.EndsWith(".arius"))
            ////{
            ////    // Restore one file

            ////}
            //else
            //{
            //    throw new NotImplementedException();
            //}

            return 0;
        }
    }

    internal class SynchronzeBlockProvider
    {
        private readonly ILogger<SynchronzeBlockProvider> _logger;
        private readonly AzureRepository _repo;

        public SynchronzeBlockProvider(ILogger<SynchronzeBlockProvider> logger, AzureRepository repo)
        {
            _logger = logger;
            _repo = repo;
        }

        /// <summary>
        /// Synchronize the local root to the remote repository
        /// </summary>
        /// <returns></returns>
        public TransformManyBlock<DirectoryInfo, PointerFile> GetBlock()
        {
            return new TransformManyBlock<DirectoryInfo, PointerFile>(async item =>
            {
                var pfes = await _repo.GetCurrentEntriesAsync(false);
                _logger.LogInformation($"{pfes.Count()} files in latest version of remote");


                var t1 = Task.Run(() => SynchronizeLocalWithRemote(pfes));
                var t2 = Task.Run(RemoveDeletedPointers);

                Task.WaitAll(t1, t2);

                return await t1;
            });

        }

        private Task<IEnumerable<PointerFile>> SynchronizeLocalWithRemote(IEnumerable<AzureRepository.PointerFileEntry> pfes)
        {
            //    //1. POINTERS THAT EXIST REMOTE BUT NOT LOCAL --> TO BE CREATED
            //    var pointersThatShouldExist = pfes.AsParallelWithParallelism()
            //        .Select(pfe => pfe.CreatePointerFileIfNotExists(_root))
            //});
            ////var createdPointers = pointerEntriesperManifest
            ////    .AsParallelWithParallelism()
            ////    .SelectMany(p => p.Value
            ////        .Where(pfe => !_localRoot.GetPointerFileInfo(pfe).Exists)
            ////        .Select(pfe =>
            ////        {
            ////            var apf = _pointerService.CreatePointerFile(_localRoot, pfe, p.Key);
            ////            _logger.LogInformation($"Pointer '{apf.RelativeName}' created");

            ////            return apf;
            ////        }))
            ////    .ToImmutableArray();

            throw new NotImplementedException();
        }

        private Task RemoveDeletedPointers()
        {
            //// 2. POINTERS THAT EXIST LOCAL BUT NOT REMOTE --> TO BE DELETED
            //var relativeNamesThatShouldExist = pointerEntriesperManifest.Values
            //    .SelectMany(x => x)
            //    .Select(x => x.RelativeName); //root.GetFullName(x));

            //_localRoot.GetAll().OfType<IPointerFile>()
            //    .AsParallelWithParallelism()
            //    .Where(pf => !relativeNamesThatShouldExist.Contains(pf.RelativeName))
            //    .ForAll(pf =>
            //    {
            //        pf.Delete();

            //        Console.WriteLine($"Pointer for '{pf.RelativeName}' deleted");
            //    });

            //_localRoot.DeleteEmptySubdirectories();

            return Task.CompletedTask;
        }
    }

    internal class DownloadBlockProvider
    {



        private void Download()
        {
            throw new NotImplementedException();


            //var pointerFiles = _localRoot.GetAll().OfType<IPointerFile>().ToImmutableArray();

            //var pointerFilesPerManifest = pointerFiles
            //    .AsParallelWithParallelism()
            //    .Where(pf => !pf.LocalContentFileInfo.Exists) //TODO test dit + same hash?
            //    .GroupBy(pf => pf.Hash)
            //    .ToImmutableDictionary(
            //        g =>
            //        {
            //            var hashValue = g.Key;
            //            var manifestFile = _manifestRepository.GetById(hashValue);
            //            return _manifestService.ReadManifestFile(manifestFile);
            //        },
            //        g => g.ToList());

            ////TODO QUID FILES THAT ALREADY EXIST / WITH DIFFERNT HASH?

            //var chunksToDownload = pointerFilesPerManifest.Keys
            //    .AsParallelWithParallelism()
            //    .SelectMany(mf => mf.ChunkNames)
            //    .Distinct()
            //    .Select(chunkName => _chunkRepository.GetByName(chunkName))
            //    .ToImmutableArray();

            //var canDownloadAll = chunksToDownload.All(c => c.CanDownload());

            //if (!canDownloadAll)
            //{
            //    _logger.LogCritical("Some blobs are still being rehydrated from Archive storage. Try again later.");
            //    return;
            //}

            //chunksToDownload = chunksToDownload.Select(c => c.Hydrated).ToImmutableArray();

            //var encryptedChunks = _chunkRepository.DownloadAll(chunksToDownload);

            //var unencryptedChunks = encryptedChunks
            //    .AsParallelWithParallelism()
            //    .Select(ec => (IChunkFile)_encrypter.Decrypt(ec, true))
            //    .ToImmutableDictionary(
            //        c => c.Hash,
            //        c => c);

            //var pointersWithChunks = pointerFilesPerManifest.Keys
            //    .GroupBy(mf => new HashValue {Value = mf.Hash})
            //    .Select(
            //        g => new
            //        {
            //            PointerFileEntry = g.SelectMany(m => m.PointerFileEntries).ToImmutableArray(),
            //            UnencryptedChunks = g.SelectMany(m => m.ChunkNames.Select(ecn => unencryptedChunks[g.Key])).ToImmutableArray()
            //        })
            //    .ToImmutableArray();

            //pointersWithChunks
            //    .AsParallelWithParallelism()
            //    .ForAll(p => Restore(_localRoot, p.PointerFileEntry, p.UnencryptedChunks));

            //if (!_options.KeepPointers)
            //    pointerFiles.AsParallel().ForAll(apf => apf.Delete());
        }
    }

    class RestoreBlockProvider
    {

        //private void Restore(LocalRootRepository root, ImmutableArray<Manifest.PointerFileEntry> pfes, ImmutableArray<IChunkFile> chunks)
        //{
        //    if (chunks.Length == 1)
        //    {
        //        //No dedup
        //        var chunk = chunks.Single();
        //        var chunkFileInfo = new FileInfo(chunk.FullName);

        //        for (int i = 0; i < pfes.Length; i++)
        //        {
        //            var pfe = pfes[i];
        //            var targetFileInfo = _localRoot.GetLocalContentFileInfo(pfe);

        //            if (i == 0)
        //                chunkFileInfo.MoveTo(targetFileInfo.FullName);
        //            else
        //                chunkFileInfo.CopyTo(targetFileInfo.FullName);

        //            targetFileInfo.CreationTimeUtc = pfe.CreationTimeUtc!.Value;
        //            targetFileInfo.LastWriteTimeUtc = pfe.LastWriteTimeUtc!.Value;
        //        }
        //    }
        //    else
        //    {
        //        throw new NotImplementedException();
        //    }
        //}
    }
}
