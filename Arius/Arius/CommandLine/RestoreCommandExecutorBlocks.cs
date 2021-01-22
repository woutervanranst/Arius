using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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

        private ITargetBlock<ChunkFile> _reconcileBlock;
        private ITargetBlock<RemoteEncryptedChunkBlobItem> _hydrateBlock;
        private ITargetBlock<RemoteEncryptedChunkBlobItem> _downloadBlock;
        private ITargetBlock<EncryptedChunkFile> _decryptBlock;

        public enum PointerState
        {
            Restored, // Pointer already restored
            NotYetMerged, // Chunks to be merged
            NotYetDecrypted, // Chunks to be decrypted
            NotYetDownloaded, // Chunks to be downloaded
            NotYetHydrated // Chunks to be hydrated from archive storage
        }

        public ProcessPointerChunksBlockProvider SetReconcileBlock(ITargetBlock<ChunkFile> reconcileBlock)
        {
            _reconcileBlock = reconcileBlock;
            return this;
        }
        public ProcessPointerChunksBlockProvider SetHydrateBlock(ITargetBlock<RemoteEncryptedChunkBlobItem> hydrateBlock)
        {
            _hydrateBlock = hydrateBlock;
            return this;
        }
        public ProcessPointerChunksBlockProvider SetEnqueueDownloadBlock(ITargetBlock<RemoteEncryptedChunkBlobItem> downloadBlock)
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
                    if (new FileInfo(Path.Combine(_downloadTempDir.FullName, $"{chunkHash.Value}{ChunkFile.Extension}")) is var cffi && cffi.Exists)
                    {
                        // R601
                        _logger.LogInformation($"Chunk {chunkHash.Value} of {pf.RelativeName} already downloaded & decrypting. Ready for merge.");
                        atLeastOneToMerge = true;
                        _reconcileBlock.Post(new ChunkFile(_root, cffi, chunkHash));
                        continue;
                    }

                    // Chunk already downloaded but not yet decryped?
                    if (new FileInfo(Path.Combine(_downloadTempDir.FullName, $"{chunkHash.Value}{EncryptedChunkFile.Extension}")) is var ecffi && ecffi.Exists)
                    {
                        // R70
                        _logger.LogInformation($"Chunk {chunkHash.Value} of {pf.RelativeName} already downloaded but not yet decrypted. Decrypting.");
                        atLeastOneToDecrypt = true;
                        _decryptBlock.Post(new EncryptedChunkFile(_root, ecffi, chunkHash));
                        continue;
                    }

                    // Chunk hydrated (in Hot/Cold stroage) but not yet downloaded?
                    if (_repo.GetHydratedChunkBlobItemByHash(chunkHash) is var hrecbi && hrecbi.Downloadable)
                    {
                        // R80
                        _logger.LogInformation($"Chunk {chunkHash.Value} of {pf.RelativeName} not yet downloaded. Queueing for download.");
                        atLeastOneToDownload = true;
                        _downloadBlock.Post(hrecbi);
                        continue;
                    }

                    // Chunk not yet hydrated
                    if (_repo.GetArchiveTierChunkBlobItemByHash(chunkHash) is var arecbi)
                    {
                        // R90
                        _logger.LogInformation($"Chunk {chunkHash.Value} of {pf.RelativeName} in archive tier. Hydrating.");
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
        public DownloadBlockProvider(RestoreOptions options,
            IConfiguration config, 
            AzureRepository repo)
        {
            _config = config;
            _repo = repo;

            var root = new DirectoryInfo(options.Path);
            _downloadTempDir = config.DownloadTempDir(root);
        }

        private readonly IConfiguration _config;
        private readonly AzureRepository _repo;
        private readonly DirectoryInfo _downloadTempDir;

        private readonly List<HashValue> _downloadedOrDownloading = new(); //Key = ChunkHashValue
        private BlockingCollection<KeyValuePair<HashValue, RemoteEncryptedChunkBlobItem>> _downloadQueue = new(); //Key = ChunkHashValue

        public ActionBlock<RemoteEncryptedChunkBlobItem> GetEnqueueBlock()
        {
            //lock (_enqueueBlock)
            //{
            if (_enqueueBlock is null)
            {
                _enqueueBlock = new(recbi =>
                {
                    lock (_downloadQueue)
                    {
                        lock (_downloadedOrDownloading)
                        {
                            if (!(_downloadQueue.Select(kvp => kvp.Key).Contains(recbi.Hash) ||
                                _downloadedOrDownloading.Contains(recbi.Hash)))
                            {
                                    // Chunk is not yet downloaded or being downlaoded -- add to queue
                                    _downloadQueue.Add(new(recbi.Hash, recbi));
                            }
                        }
                    }
                });

                _enqueueBlock.Completion.ContinueWith(_ => _downloadQueue.CompleteAdding()); //R811
            }
            //}

            return _enqueueBlock;
        }
        private ActionBlock<RemoteEncryptedChunkBlobItem> _enqueueBlock = null;


        public Task GetBatchingTask()
        {
            //lock (_createBatchTask)
            //{
            if (_createBatchTask is null)
            {
                _createBatchTask = Task.Run(() =>
                {
                    Thread.CurrentThread.Name = "Download Batcher";

                    while (!GetEnqueueBlock().Completion.IsCompleted ||
                           !_downloadQueue.IsCompleted) //_downloadQueue.Count > 0)
                    {
                        var batch = new List<RemoteEncryptedChunkBlobItem>();
                        long size = 0;

                        foreach (var item in _downloadQueue.GetConsumingEnumerable())
                        {
                            lock (_downloadedOrDownloading)
                            {
                                // WARNING potential thread safety issue? where element is taken from the queue and just after the GetEnqueueBlock() method starts checking the Contains
                                _downloadedOrDownloading.Add(item.Key);
                            }

                            batch.Add(item.Value);
                            size += item.Value.Length;

                            if (size >= _config.BatchSize ||
                                batch.Count >= _config.BatchCount ||
                                _downloadQueue.IsCompleted) //if we re at the end of the queue, upload the remainder
                                break;
                        }

                        // TODO // IF SOURCE COMPLETED + THIS EMPTY SET TO COMPLETE ?

                        //Emit a batch
                        if (batch.Any()) //op het einde - als alles al gedownload was
                            GetDownloadBlock().Post(batch.ToArray()); //R812
                    }

                    GetDownloadBlock().Complete(); //R813
                });
                //}
            }

            return _createBatchTask;
        }
        private Task _createBatchTask = null;



        public TransformManyBlock<RemoteEncryptedChunkBlobItem[], EncryptedChunkFile> GetDownloadBlock()
        {
            //lock (_downloadBlock)
            //{
            if (_downloadBlock is null)
            {
                _downloadBlock = new(batch =>
                {
                    //Download this batch
                    var ecfs = _repo.Download(batch, _downloadTempDir, true);
                    return ecfs;

                }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 2 });
            }
            //}

            return _downloadBlock;
        }
        private TransformManyBlock<RemoteEncryptedChunkBlobItem[], EncryptedChunkFile> _downloadBlock;
    }


    internal class DecryptBlockProvider
    {
        private readonly IEncrypter _encrypter;

        public DecryptBlockProvider(IEncrypter encrypter)
        {
            _encrypter = encrypter;
        }

        public TransformBlock<EncryptedChunkFile, ChunkFile> GetBlock()
        {
            return new(ecf =>
            {
                var targetFile = new FileInfo($"{ecf.FullName.TrimEnd(EncryptedChunkFile.Extension)}{ChunkFile.Extension}");

                _encrypter.Decrypt(ecf, targetFile, true);

                var cf = new ChunkFile(null, targetFile, ecf.Hash);

                return cf;
            });
        }
    }

    internal class ReconcilePointersWithChunksBlockProvider
    {
        private readonly Dictionary<HashValue, ChunkFile> _processedChunks = new();
        private readonly Dictionary<PointerFile, List<HashValue>> _inFlightPointers = new();

        public ActionBlock<PointerFile> GetReconcilePointerBlock()
        {
            if (_reconcilePointerBlock is null)
            {
                _reconcilePointerBlock = new(pf => { 

                });
            }

            return _reconcilePointerBlock;
        }

        private ActionBlock<PointerFile> _reconcilePointerBlock = null;

        public ActionBlock<ChunkFile> GetReconcileChunkBlock()
        {
            return new(cf =>
            {

            });
        }

        public TransformManyBlock<object, (PointerFile, ChunkFile[])> GetBlock()
        {
            return new(item =>
            {
                lock (_processedChunks)
                {
                    lock (_inFlightPointers)
                    {
                        if (item is PointerFile pf) //A pointer from R60
                        {
                            var chunksStillWaitingFor = pf.ChunkHashes.Except(_processedChunks.Keys).ToList();
                            _inFlightPointers.Add(pf, chunksStillWaitingFor); //TODO
                        }
                        else if (item is ChunkFile cf) //A chunk from R72
                        {
                            _processedChunks.Add(cf.Hash, cf);

                            foreach (var requiredChunks in _inFlightPointers.Values.Where(kvp => kvp.Contains(cf.Hash)))
                                requiredChunks.Remove(cf.Hash);
                        }
                        else
                            throw new InvalidOperationException(); //TODO

                        // Determine if there are pointers that are ready to restore (not list of chunkvalues is empty)
                        var readyPointers = _inFlightPointers.Where(a => !a.Value.Any()).Select(a => a.Key).ToArray();

                        // Remove ready pointers from the in flight pointers
                        foreach (var readyPointer in readyPointers)
                            _inFlightPointers.Remove(readyPointer);

                        if (readyPointers.Any())
                        {
                            var r = readyPointers.Select(rp => (rp, rp.ChunkHashes.Select(ch => _processedChunks[ch]).ToArray()));
                            return r;
                        }
                        else
                            return Enumerable.Empty<(PointerFile, ChunkFile[])>();
                    }
                }
            });
        }
    }

    
    internal class MergeBlockProvider
    {
        public MergeBlockProvider(IChunker chunker)
        {
            _chunker = chunker;
        }

        private readonly IChunker _chunker;

        public ActionBlock<(PointerFile, ChunkFile[])> GetBlock()
        {
            return new(item => {
                (PointerFile pf, ChunkFile[] chunks) = item;

                _chunker.Merge(chunks);



                ////pointersWithChunks
                ////    .AsParallelWithParallelism()
                ////    .ForAll(p => Restore(_localRoot, p.PointerFileEntry, p.UnencryptedChunks));

                ////if (!_options.KeepPointers)
                ////    pointerFiles.AsParallel().ForAll(apf => apf.Delete());


            });
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

