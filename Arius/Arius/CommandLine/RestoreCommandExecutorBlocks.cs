using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;
using Arius.Services;
using Microsoft.Extensions.Logging;
using Enumerable = System.Linq.Enumerable;

namespace Arius.CommandLine
{
    internal class SynchronizeBlockProvider
    {
        private readonly ILogger<SynchronizeBlockProvider> _logger;
        private readonly DirectoryInfo _root;
        private readonly AzureRepository _repo;
        private readonly PointerService _ps;

        public SynchronizeBlockProvider(ILogger<SynchronizeBlockProvider> logger, RestoreOptions options, AzureRepository repo, PointerService ps)
        {
            _logger = logger;
            _root = new DirectoryInfo(options.Path);
            _repo = repo;
            _ps = ps;
        }

        /// <summary>
        /// Synchronize the local root to the remote repository
        /// </summary>
        /// <returns></returns>
        public TransformManyBlock<DirectoryInfo, PointerFile> GetBlock()
        {
            return new TransformManyBlock<DirectoryInfo, PointerFile>(async item =>
            {
                var currentPfes = await _repo.GetCurrentEntriesAsync(false);
                currentPfes = currentPfes.ToArray();

                _logger.LogInformation($"{currentPfes.Count()} files in latest version of remote");

                var t1 = Task.Run(() => CreateIfNotExists(currentPfes));
                var t2 = Task.Run(() => DeleteIfExists(currentPfes));

                Task.WaitAll(t1, t2);

                return await t1;
            });

        }

        /// <summary>
        /// Get the PointerFiles for the given PointerFileEntries. Create PointerFiles if they do not exist.
        /// </summary>
        /// <returns></returns>
        private IEnumerable<PointerFile> CreateIfNotExists(IEnumerable<AzureRepository.PointerFileEntry> pfes)
        {
            var pfs = pfes
                .AsParallelWithParallelism()
                .Select(pfe => _ps.CreatePointerFileIfNotExists(_root, pfe));

            return pfs.ToArray();
        }

        /// <summary>
        /// Delete the PointerFiles that do not exist in the given PointerFileEntries.
        /// </summary>
        /// <param name="pfes"></param>
        private void DeleteIfExists(IEnumerable<AzureRepository.PointerFileEntry> pfes)
        {
            var relativeNames = pfes.Select(pfe => pfe.RelativeName).ToArray();

            Parallel.ForEach(_root.GetFiles($"*{PointerFile.Extension}", SearchOption.AllDirectories), pfi =>
            {
                var relativeName = Path.GetRelativePath(_root.FullName, pfi.FullName);

                if (!relativeNames.Contains(relativeName))
                {
                    pfi.Delete();

                    _logger.LogInformation($"Pointer for '{relativeName}' deleted");
                }
            });

            _root.DeleteEmptySubdirectories();
        }
    }

    //    internal class GetCunksToDownloadPerPointerFileBlockProvider
    //    {
    //        public GetCunksToDownloadPerPointerFileBlockProvider(RestoreOptions options, 
    //            IConfiguration config, 
    //            ILogger<GetCunksToDownloadPerPointerFileBlockProvider> logger, 
    //            IHashValueProvider hvp,
    //            )
    //        {
    //            _options = options;
    //            _config = config;
    //            _logger = logger;
    //            _hvp = hvp;
    //            _repo = repo;

    //            _root = new DirectoryInfo(options.Path);
    //        }

    //        private readonly RestoreOptions _options;
    //        private readonly IConfiguration _config;
    //        private readonly ILogger<GetCunksToDownloadPerPointerFileBlockProvider> _logger;
    //        private readonly IHashValueProvider _hvp;
    //        private readonly AzureRepository _repo;
    //        private readonly DirectoryInfo _root;


    //        public GetCunksToDownloadPerPointerFileBlockProvider AddAndInitializeAlreadyDownloaded(Dictionary<HashValue, IChunkFile> alreadyDownloaded)
    //        {
    //            foreach (var fi in _config.DownloadTempDir(_root).GetFiles())
    //            {
    //                if (fi.Name.EndsWith(EncryptedChunkFile.Extension))
    //                {
    //                    var ecf = new EncryptedChunkFile(_root, fi, new HashValue() {Value = fi.Name.TrimEnd(EncryptedChunkFile.Extension)});
    //                    alreadyDownloaded.Add(ecf.Hash, ecf);
    //                }
    //                else if (fi.Name.EndsWith(ChunkFile.Extension))
    //                {
    //                    var cf = new ChunkFile(_root, fi, new HashValue() {Value = fi.Name.TrimEnd(EncryptedChunkFile.Extension)});
    //                    alreadyDownloaded.Add(cf.Hash, cf);
    //                }
    //            }

    //            return this;
    //        }

    enum PointerState
    {
        Restored,
        //ToHydrate,
        //Hydrating,
        NotYetDownloaded,
        //ChunksDownloaded
        NotYetHydrated,
        NotYetDecrypted,
        NotYetMerged
    }
    
    internal class ProcessPointerChunksBlockProvider
    {
        public ProcessPointerChunksBlockProvider(
            ILogger<ProcessPointerChunksBlockProvider> logger, 
            IConfiguration config,
            RestoreOptions options,
            IHashValueProvider hvp,
            AzureRepository repo)
        {
            _logger = logger;
            _hvp = hvp;
            _repo = repo;

            _root = new DirectoryInfo(options.Path);
            _downloadTempDir = config.DownloadTempDir(_root);
        }

        private readonly ILogger<ProcessPointerChunksBlockProvider> _logger;
        private readonly IHashValueProvider _hvp;
        private readonly AzureRepository _repo;
        private readonly DirectoryInfo _root;
        private readonly DirectoryInfo _downloadTempDir;

        //private BlockingCollection<RemoteEncryptedChunkBlobItem> _hydrateQueue;
        //private BlockingCollection<RemoteEncryptedChunkBlobItem> _downloadQueue;
        //private BlockingCollection<EncryptedChunkFile> _decryptQueue;

        //public ProcessPointerChunksBlockProvider AddHydrateQueue(BlockingCollection<RemoteEncryptedChunkBlobItem> hydrateQueue)
        //{
        //    _hydrateQueue = hydrateQueue;
        //    return this;
        //}
        //public ProcessPointerChunksBlockProvider AddDownloadQueue(BlockingCollection<RemoteEncryptedChunkBlobItem> downloadQueue)
        //{
        //    _downloadQueue = downloadQueue;
        //    return this;
        //}
        //public ProcessPointerChunksBlockProvider AddDecryptQueue(BlockingCollection<EncryptedChunkFile> decryptQueue)
        //{
        //    _decryptQueue = decryptQueue;
        //    return this;
        //}

        private ITargetBlock<RemoteEncryptedChunkBlobItem> _hydrateBlock;
        private ITargetBlock<RemoteEncryptedChunkBlobItem> _downloadBlock;
        private ITargetBlock<EncryptedChunkFile> _decryptBlock;

        public ProcessPointerChunksBlockProvider SetHydrateBlock(ITargetBlock<RemoteEncryptedChunkBlobItem> hydrateBlock)
        {
            _hydrateBlock = hydrateBlock;
            return this;
        }
        public ProcessPointerChunksBlockProvider SetDownloadBlock(ITargetBlock<RemoteEncryptedChunkBlobItem> downloadBlock)
        {
            _downloadBlock = downloadBlock;
            return this;
        }
        public ProcessPointerChunksBlockProvider SetDecryptBlock(ITargetBlock<EncryptedChunkFile> decryptBlock)
        {
            _decryptBlock = decryptBlock;
            return this;
        }

        public TransformBlock<PointerFile, (PointerFile PointerFile, PointerState State)> GetBlock()
        {
            return new(pf =>
            {
                // Chunks Downloaded & Merged?
                if (pf.BinaryFileInfo is var bfi && bfi.Exists &&
                    new BinaryFile(pf.Root, bfi) is var bf && _hvp.GetHashValue(bf).Equals(pf.Hash))
                {
                    _logger.LogInformation($"PointerFile {pf.RelativeName} already downloaded - skipping");
                    return (pf, PointerState.Restored);

                    //TODO TEST: binary file already exist - do not 
                }
                
                pf.ChunkHashes = _repo.GetChunkHashes(pf.Hash).ToArray();

                bool atLeastOneToMerge = false, atLeastOneToDecrypt = false, atLeastOneToDownload = false, atLeastOneToHydrate = false;

                foreach (var chunkHash in pf.ChunkHashes)
                {
                    // Chunk already downloaded & decrypted?
                    if (new FileInfo(Path.Combine(_downloadTempDir.FullName, $"{chunkHash.Value}.{ChunkFile.Extension}")) is var cffi && cffi.Exists)
                    {
                        atLeastOneToMerge = true;
                        continue;
                    }

                    // Chunk already downloaded but not yet decryped?
                    if (new FileInfo(Path.Combine(_downloadTempDir.FullName, $"{chunkHash.Value}.{EncryptedChunkFile.Extension}")) is var ecffi && ecffi.Exists)
                    {
                        // 70
                        atLeastOneToDecrypt = true;
                        _decryptBlock.Post(new EncryptedChunkFile(_root, ecffi, chunkHash));
                        continue;
                    }

                    // Chunk hydrated (in Hot/Cold stroage) but not yet downloaded?
                    if (_repo.GetHydratedChunkBlobItemByHash(chunkHash) is var hrecbi && hrecbi.Downloadable)
                    {
                        // 80
                        atLeastOneToDownload = true;
                        _downloadBlock.Post(hrecbi);
                        continue;
                    }

                    // Chunk not yet hydrated
                    if (_repo.GetArchiveTierChunkBlobItemByHash(chunkHash) is var arecbi)
                    {
                        // 90
                        atLeastOneToHydrate = true;
                        _hydrateBlock.Post(arecbi);
                        continue;
                    }
                }

                if (atLeastOneToHydrate)
                    // At least one chunk is waiting for hydration
                    return (pf, PointerState.NotYetHydrated);
                else if (atLeastOneToDownload)
                    // At least one chunk is waiting for download
                    return (pf, PointerState.NotYetDownloaded);
                else if (atLeastOneToDecrypt)
                    // At least one chunk is waiting for decryption
                    return (pf, PointerState.NotYetDecrypted);
                else if (atLeastOneToMerge)
                    // At least one chunk needs to be merged
                    return (pf, PointerState.NotYetMerged);
                else
                    throw new ApplicationException("huh?"); //TODO
            });
        }
    }

    internal class HydrateBlockProvider
    {
        private readonly AzureRepository _repo;

        public HydrateBlockProvider(AzureRepository repo)
        {
            _repo = repo;
        }
        public ActionBlock<RemoteEncryptedChunkBlobItem> GetBlock()
        {
            return new(recbi =>
            {
                _repo.Hydrate(recbi);

                AtLeastOneHydrated = true;
            });
        }

        public bool AtLeastOneHydrated { get; private set; }
    }
    
    internal class DownloadBlockProvider
    {
        public DownloadBlockProvider(IConfiguration config, AzureRepository repo)
        {
            _config = config;
            _repo = repo;
        }

        public DownloadBlockProvider AddSourceBlock(ISourceBlock<PointerFile> source)
        {
            _source = source;

            return this;
        }

        private readonly Dictionary<HashValue, RemoteEncryptedChunkBlobItem> _notYetDownloading = new(); //Key = ChunkHashValue
        //private readonly List<HashValue> _notYetDownloading = new(); //Key = ChunkHashValue
        private readonly List<HashValue> _downloadedOrDownloading = new(); //Key = ChunkHashValue
        private readonly IConfiguration _config;
        private readonly AzureRepository _repo;

        private ISourceBlock<PointerFile> _source;

        public TransformManyBlock<RemoteEncryptedChunkBlobItem, EncryptedChunkFile> GetBlock()
        {
            return new(pf =>
            {
                return Enumerable.Empty<EncryptedChunkFile>();

                //var chunkHashValues = _repo.GetChunkHashes(pf.Hash);

                //lock (_notYetDownloading)
                //{
                //    lock (_downloadedOrDownloading)
                //    {
                //        foreach (var chunkHash in chunkHashValues)
                //        {
                //            if (!(_notYetDownloading.ContainsKey(chunkHash) || _downloadedOrDownloading.Contains(chunkHash)))
                //            {

                //                throw new NotImplementedException();
                //                //var recbi = _repo.GetChunkBlobItemByHash(chunkHash);

                //                //_notYetDownloading.Add(chunkHash, recbi);
                //            }
                //        }

                //        if (_notYetDownloading.Values.Sum(recbi => recbi.Length) >= _config.BatchSize ||
                //            _notYetDownloading.Count >= _config.BatchCount ||
                //            _source.Completion.IsCompleted)
                //        {
                //            //Emit a batch
                //            var batch = new[] {_notYetDownloading.Values.ToArray()};

                //            _downloadedOrDownloading.AddRange(_notYetDownloading.Keys);
                //            _notYetDownloading.Clear();

                //            // IF SOURCE COMPLETED + THIS EMPTY SET TO COMPLETE ?

                //            return batch;
                //        }
                //        else
                //        {
                //            //Wait unil more values accumulate
                //            return Enumerable.Empty<RemoteEncryptedChunkBlobItem[]>();
                //        }
                //    }
                //}

                ////var pointerFiles = _localRoot.GetAll().OfType<IPointerFile>().ToImmutableArray();

                ////var pointerFilesPerManifest = pointerFiles
                ////    .AsParallelWithParallelism()
                ////    .Where(pf => !pf.LocalContentFileInfo.Exists) //TODO test dit + same hash?
                ////    .GroupBy(pf => pf.Hash)
                ////    .ToImmutableDictionary(
                ////        g =>
                ////        {
                ////            var hashValue = g.Key;
                ////            var manifestFile = _manifestRepository.GetById(hashValue);
                ////            return _manifestService.ReadManifestFile(manifestFile);
                ////        },
                ////        g => g.ToList());

                //////TODO QUID FILES THAT ALREADY EXIST / WITH DIFFERNT HASH?

                ////var chunksToDownload = pointerFilesPerManifest.Keys
                ////    .AsParallelWithParallelism()
                ////    .SelectMany(mf => mf.ChunkNames)
                ////    .Distinct()
                ////    .Select(chunkName => _chunkRepository.GetByName(chunkName))
                ////    .ToImmutableArray();

                ////var canDownloadAll = chunksToDownload.All(c => c.CanDownload());

                ////if (!canDownloadAll)
                ////{
                ////    _logger.LogCritical("Some blobs are still being rehydrated from Archive storage. Try again later.");
                ////    return;
                ////}

                ////chunksToDownload = chunksToDownload.Select(c => c.Hydrated).ToImmutableArray();

                ////var encryptedChunks = _chunkRepository.DownloadAll(chunksToDownload);

                ////var unencryptedChunks = encryptedChunks
                ////    .AsParallelWithParallelism()
                ////    .Select(ec => (IChunkFile)_encrypter.Decrypt(ec, true))
                ////    .ToImmutableDictionary(
                ////        c => c.Hash,
                ////        c => c);

                ////var pointersWithChunks = pointerFilesPerManifest.Keys
                ////    .GroupBy(mf => new HashValue {Value = mf.Hash})
                ////    .Select(
                ////        g => new
                ////        {
                ////            PointerFileEntry = g.SelectMany(m => m.PointerFileEntries).ToImmutableArray(),
                ////            UnencryptedChunks = g.SelectMany(m => m.ChunkNames.Select(ecn => unencryptedChunks[g.Key])).ToImmutableArray()
                ////        })
                ////    .ToImmutableArray();

                ////pointersWithChunks
                ////    .AsParallelWithParallelism()
                ////    .ForAll(p => Restore(_localRoot, p.PointerFileEntry, p.UnencryptedChunks));

                ////if (!_options.KeepPointers)
                ////    pointerFiles.AsParallel().ForAll(apf => apf.Delete());
            });
        }
    }

    internal class DecryptBlockProvider
    {
        public TransformBlock<EncryptedChunkFile, ChunkFile> GetBlock()
        {
            throw new NotImplementedException();
        }
    }

    internal class ReconcilePointersWithChunksBlockProvider
    {
        public TransformManyBlock<ChunkFile, PointerFile> GetBlock()
        {
            throw new NotImplementedException();
        }
    }

    
    internal class MergeBlockProvider
    {
        public ActionBlock<PointerFile> GetBlock()
        {
            throw new NotImplementedException();
        }


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
