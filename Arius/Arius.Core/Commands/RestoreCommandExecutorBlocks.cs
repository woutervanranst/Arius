using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Core.Extensions;
using Arius.Models;
using Arius.Repositories;
using Arius.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Enumerable = System.Linq.Enumerable;

namespace Arius.Core.Commands
{
    internal class SynchronizeBlockProvider
    {
        public SynchronizeBlockProvider(ILogger<SynchronizeBlockProvider> logger, RestoreCommandOptions options, AzureRepository repo, PointerService ps)
        {
            _logger = logger;
            _root = new DirectoryInfo(options.Path);
            _repo = repo;
            _ps = ps;
        }

        private readonly ILogger<SynchronizeBlockProvider> _logger;
        private readonly DirectoryInfo _root;
        private readonly AzureRepository _repo;
        private readonly PointerService _ps;

        /// <summary>
        /// Synchronize the local root to the remote repository
        /// </summary>
        /// <returns></returns>
        public TransformManyBlock<DirectoryInfo, PointerFile> GetBlock()
        {
            return new TransformManyBlock<DirectoryInfo, PointerFile>(async item =>
            {
                var currentPfes = await _repo.GetCurrentEntries(false);
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
        public ProcessPointerChunksBlockProvider(ILogger<ProcessPointerChunksBlockProvider> logger, IOptions<ITempDirectoryAppSettings> tempDirAppSettings, RestoreCommandOptions options,
            IHashValueProvider hvp,
            AzureRepository repo)
        {
            _logger = logger;
            _hvp = hvp;
            _repo = repo;

            _downloadTempDir = tempDirAppSettings.Value.RestoreTempDirectory(new DirectoryInfo(options.Path));
        }

        private readonly ILogger<ProcessPointerChunksBlockProvider> _logger;
        private readonly IHashValueProvider _hvp;
        private readonly AzureRepository _repo;
        private readonly DirectoryInfo _downloadTempDir;

        private ITargetBlock<ChunkFile> _reconcileChunkBlock;
        private ITargetBlock<ChunkBlobBase> _hydrateBlock;
        private ITargetBlock<ChunkBlobBase> _downloadBlock;
        private ITargetBlock<EncryptedChunkFile> _decryptBlock;

        public enum PointerState
        {
            Restored, // Pointer already restored
            Restoring, // Manifest already being restored
            NotYetMerged, // Chunks to be merged
            NotYetDecrypted, // Chunks to be decrypted
            NotYetDownloaded, // Chunks to be downloaded
            NotYetHydrated // Chunks to be hydrated from archive storage
        }

        public ProcessPointerChunksBlockProvider SetReconcileChunkBlock(ITargetBlock<ChunkFile> reconcileChunkBlock)
        {
            _reconcileChunkBlock = reconcileChunkBlock;
            return this;
        }
        public ProcessPointerChunksBlockProvider SetHydrateBlock(ITargetBlock<ChunkBlobBase> hydrateBlock)
        {
            _hydrateBlock = hydrateBlock;
            return this;
        }
        public ProcessPointerChunksBlockProvider SetEnqueueDownloadBlock(ITargetBlock<ChunkBlobBase> downloadBlock)
        {
            _downloadBlock = downloadBlock;
            return this;
        }
        public ProcessPointerChunksBlockProvider SetDecryptBlock(ITargetBlock<EncryptedChunkFile> decryptBlock)
        {
            _decryptBlock = decryptBlock;
            return this;
        }

        private readonly List<HashValue> _inFlightManifests = new();

        public TransformBlock<PointerFile, (PointerFile PointerFile, PointerState State)> GetBlock()
        {
            return new(async pf =>
            {
                lock (_inFlightManifests)
                {
                    if (_inFlightManifests.Contains(pf.Hash))
                    {
                        return (pf, PointerState.Restoring);
                    }

                    _inFlightManifests.Add(pf.Hash);
                }

                // Chunks Downloaded & Merged?
                if (pf.BinaryFileInfo is var bfi && bfi.Exists &&
                    new BinaryFile(pf.Root, bfi) is var bf && _hvp.GetHashValue(bf).Equals(pf.Hash))
                {
                    _logger.LogInformation($"PointerFile {pf.RelativeName} already downloaded - skipping");
                    return (pf, PointerState.Restored);

                    //TODO TEST: binary file already exist - do not 
                }

                pf.ChunkHashes = (await _repo.GetChunkHashesAsync(pf.Hash)).ToArray();

                bool atLeastOneToMerge = false, atLeastOneToDecrypt = false, atLeastOneToDownload = false, atLeastOneToHydrate = false;

                foreach (var chunkHash in pf.ChunkHashes)
                {
                    // Chunk already downloaded & decrypted?
                    if (new FileInfo(Path.Combine(_downloadTempDir.FullName, $"{chunkHash.Value}{ChunkFile.Extension}")) is var cffi && cffi.Exists)
                    {
                        // R601
                        _logger.LogInformation($"Chunk {chunkHash.Value} of {pf.RelativeName} already downloaded & decrypting. Ready for merge.");
                        atLeastOneToMerge = true;
                        _reconcileChunkBlock.Post(new ChunkFile(cffi, chunkHash));
                        continue;
                    }

                    // Chunk already downloaded but not yet decryped?
                    if (new FileInfo(Path.Combine(_downloadTempDir.FullName, $"{chunkHash.Value}{EncryptedChunkFile.Extension}")) is var ecffi && ecffi.Exists)
                    {
                        // R70
                        _logger.LogInformation($"Chunk {chunkHash.Value} of {pf.RelativeName} already downloaded but not yet decrypted. Decrypting.");
                        atLeastOneToDecrypt = true;
                        _decryptBlock.Post(new EncryptedChunkFile(ecffi, chunkHash));
                        continue;
                    }

                    // Chunk hydrated (in Hot/Cold stroage) but not yet downloaded?
                    if (_repo.GetChunkBlobByHash(chunkHash, true) is var hydratedChunk &&
                        hydratedChunk is not null &&
                        hydratedChunk.Downloadable)
                    {
                        // R80
                        _logger.LogInformation($"Chunk {chunkHash.Value} of {pf.RelativeName} not yet downloaded. Queueing for download.");
                        atLeastOneToDownload = true;
                        _downloadBlock.Post(hydratedChunk);
                        continue;
                    }

                    // Chunk not yet hydrated
                    if (_repo.GetChunkBlobByHash(chunkHash, false) is var archiveTierChunk)
                    {
                        // R90
                        _logger.LogInformation($"Chunk {chunkHash.Value} of {pf.RelativeName} in Archive tier. Starting hydration or getting hydration status...");
                        atLeastOneToHydrate = true;
                        _hydrateBlock.Post(archiveTierChunk);
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
        public ActionBlock<ChunkBlobBase> GetBlock()
        {
            return new(chunkBlob =>
            {
                _repo.Hydrate(chunkBlob);

                AtLeastOneHydrating = true;
            });
        }

        public bool AtLeastOneHydrating { get; private set; }
    }

    internal class DownloadBlockProvider
    {
        public DownloadBlockProvider(RestoreCommandOptions options, IOptions<IAzCopyAppSettings> azCopyAppSettings, IOptions<ITempDirectoryAppSettings> tempDirAppSettings, AzureRepository repo)
        {
            this.azCopyAppSettings = azCopyAppSettings.Value;
            this.repo = repo;

            var root = new DirectoryInfo(options.Path);
            downloadTempDir = tempDirAppSettings.Value.RestoreTempDirectory(root);
        }

        private readonly IAzCopyAppSettings azCopyAppSettings;
        private readonly AzureRepository repo;
        private readonly DirectoryInfo downloadTempDir;

        private readonly List<HashValue> _downloadedOrDownloading = new(); //Key = ChunkHashValue
        private readonly BlockingCollection<KeyValuePair<HashValue, ChunkBlobBase>> _downloadQueue = new(); //Key = ChunkHashValue

        public ActionBlock<ChunkBlobBase> GetEnqueueBlock()
        {
            //lock (_enqueueBlock)
            //{
            if (_enqueueBlock is null)
            {
                _enqueueBlock = new(chunkBlob =>
                {
                    lock (_downloadQueue)
                    {
                        lock (_downloadedOrDownloading)
                        {
                            if (!(_downloadQueue.Select(kvp => kvp.Key).Contains(chunkBlob.Hash) ||
                                _downloadedOrDownloading.Contains(chunkBlob.Hash)))
                            {
                                // Chunk is not yet downloaded or being downlaoded -- add to queue
                                _downloadQueue.Add(new(chunkBlob.Hash, chunkBlob));
                            }
                        }
                    }
                });

                _enqueueBlock.Completion.ContinueWith(_ => _downloadQueue.CompleteAdding()); //R811
            }
            //}

            return _enqueueBlock;
        }
        private ActionBlock<ChunkBlobBase> _enqueueBlock = null; //TODO to lazy?


        public Task GetBatchingTask()
        {
            if (_createBatchTask is null)
            {
                _createBatchTask = Task.Run(() =>
                {
                    Thread.CurrentThread.Name = "Download Batcher";

                    while (!GetEnqueueBlock().Completion.IsCompleted ||
                           !_downloadQueue.IsCompleted) //_downloadQueue.Count > 0)
                    {
                        var batch = new List<ChunkBlobBase>();
                        long size = 0;

                        foreach (var kvp in _downloadQueue.GetConsumingEnumerable())
                        {
                            lock (_downloadedOrDownloading)
                            {
                                // WARNING potential thread safety issue? where element is taken from the queue and just after the GetEnqueueBlock() method starts checking the Contains
                                _downloadedOrDownloading.Add(kvp.Key);
                            }

                            batch.Add(kvp.Value);
                            size += kvp.Value.Length;

                            if (size >= azCopyAppSettings.BatchSize ||
                                batch.Count >= azCopyAppSettings.BatchCount ||
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
            }

            return _createBatchTask;
        }
        private Task _createBatchTask = null;



        public TransformManyBlock<ChunkBlobBase[], EncryptedChunkFile> GetDownloadBlock()
        {
            //lock (_downloadBlock)
            //{
            if (_downloadBlock is null)
            {
                _downloadBlock = new(batch =>
                {
                    //Download this batch
                    var ecfs = repo.Download(batch, downloadTempDir, true);
                    return ecfs;

                }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 2 });
            }
            //}

            return _downloadBlock;
        }
        private TransformManyBlock<ChunkBlobBase[], EncryptedChunkFile> _downloadBlock;
    }


    internal class DecryptBlockProvider
    {
        public DecryptBlockProvider(ILogger<DecryptBlockProvider> logger, IEncrypter encrypter)
        {
            _logger = logger;
            _encrypter = encrypter;
        }

        private readonly ILogger<DecryptBlockProvider> _logger;
        private readonly IEncrypter _encrypter;

        public TransformBlock<EncryptedChunkFile, ChunkFile> GetBlock()
        {
            return new(ecf =>
            {
                _logger.LogInformation($"Decrypting chunk {ecf.Hash}...");

                var targetFile = new FileInfo($"{ecf.FullName.TrimEnd(EncryptedChunkFile.Extension)}{ChunkFile.Extension}");

                _encrypter.Decrypt(ecf, targetFile, true);

                var cf = new ChunkFile(targetFile, ecf.Hash);

                _logger.LogInformation($"Decrypting chunk {ecf.Hash}... done");

                return cf;
            });
        }
    }

    internal class ReconcilePointersWithChunksBlockProvider
    {
        public ReconcilePointersWithChunksBlockProvider(ILogger<ReconcilePointersWithChunksBlockProvider> logger)
        {
            _logger = logger;
        }

        private readonly ILogger<ReconcilePointersWithChunksBlockProvider> _logger;

        private readonly Dictionary<HashValue, ChunkFile> _processedChunks = new();
        private readonly Dictionary<HashValue, (List<PointerFile> PointerFiles, List<HashValue> ChunkHashes)> _inFlightPointers = new(); // Key = ManifestHash

        public ActionBlock<PointerFile> GetReconcilePointerBlock()
        {
            if (_reconcilePointerBlock is null)
            {
                _reconcilePointerBlock = new(pf =>
                {
                    lock (_inFlightPointers)
                    {
                        _logger.LogInformation($"Reconciliation Pointers/Chunks - Setting up for Pointers with ManifestHash {pf.Hash}...");

                        if (!_inFlightPointers.ContainsKey(pf.Hash))
                            _inFlightPointers.Add(pf.Hash, new(new(), new()));

#pragma warning disable IDE0042 // Deconstruct variable declaration -- does not improve readability of code
                        var entry = _inFlightPointers[pf.Hash];
#pragma warning restore IDE0042 // Deconstruct variable declaration

                        if (entry.ChunkHashes.Count == 0 && pf.ChunkHashes is not null)
                        {
                            entry.ChunkHashes.AddRange(pf.ChunkHashes);
                            _logger.LogInformation($"Reconciliation Pointers/Chunks - ManifestHash {pf.Hash}... {pf.ChunkHashes.Count()} chunk(s) required");
                        }
                        else if (entry.ChunkHashes.Count > 0 && pf.ChunkHashes is not null)
                            throw new InvalidOperationException("Too many chunk hash definitions"); //the list of thunks for this manfiest should be mastered once


                        entry.PointerFiles.Add(pf);
                        _logger.LogInformation($"Reconciliation Pointers/Chunks - ManifestHash {pf.Hash}... added PointerFile {pf.RelativeName}");
                    }
                });
            }

            return _reconcilePointerBlock;
        }

        private ActionBlock<PointerFile> _reconcilePointerBlock = null;

        public TransformManyBlock<ChunkFile, (PointerFile[], ChunkFile[], ChunkFile[])> GetReconcileChunkBlock()
        {
            return new(cf =>
            {
                _processedChunks.Add(cf.Hash, cf);

                // Wait until all pointers have been reconciled and we have a full view of what chunks are needed for which files
                Task.WaitAll(_reconcilePointerBlock.Completion); // R603

                lock (_inFlightPointers)
                {
                    // Remove this chunk from all pointers that require it
                    foreach (var pointer in _inFlightPointers.Values.Where(kvp => kvp.ChunkHashes.Contains(cf.Hash)))
                    {
                        pointer.ChunkHashes.Remove(cf.Hash);
                        _logger.LogInformation($"Reconciliation Pointers/Chunks - Chunk {cf.Hash} reconciled with {pointer.PointerFiles.Count} PointerFile(s). {pointer.ChunkHashes.Count} Chunks remaining.");
                    }

                    // Determine if there are pointers that are ready to restore (not list of chunkvalues is empty)
                    var hashesOfReadyPointers = _inFlightPointers.Where(kvp => !kvp.Value.ChunkHashes.Any()).Select(kvp => kvp.Key).ToArray();

                    if (hashesOfReadyPointers.Any())
                    {
                        _logger.LogInformation($"Reconciliation Pointers/Chunks - {hashesOfReadyPointers.Length} Pointer(s) ready for merge");

                        var r = hashesOfReadyPointers.Select(manifestHash =>
                        {
                            var pointersToRestore = _inFlightPointers[manifestHash].PointerFiles.ToArray();

                            var chunkHashes = pointersToRestore.First().ChunkHashes;
                            var withChunks = chunkHashes
                                .Select(chunkHash => _processedChunks[chunkHash]).ToArray();
                            var chunksThatCanBeDeleted = chunkHashes
                                .Except(_inFlightPointers.Values.SelectMany(e => e.ChunkHashes))
                                .Select(chunkHash => _processedChunks[chunkHash]).ToArray(); //TODO TESTEN

                            return (pointersToRestore, withChunks, chunksThatCanBeDeleted);
                        }).ToArray();

                        // Remove ready pointers from the in flight pointers
                        foreach (var hash in hashesOfReadyPointers)
                            _inFlightPointers.Remove(hash);

                        return r;
                    }
                    else
                        return Enumerable.Empty<(PointerFile[], ChunkFile[], ChunkFile[])>();
                }
            });
        }
    }


    internal class MergeBlockProvider
    {
        public MergeBlockProvider(ILogger<MergeBlockProvider> logger, RestoreCommandOptions options, IHashValueProvider hvp, DedupChunker dedupChunker)
        {
            _logger = logger;
            _hvp = hvp;
            _chunker = new();
            _dedupChunker = dedupChunker;
            _keepPointers = options.KeepPointers;
        }

        private readonly Chunker _chunker;
        private readonly DedupChunker _dedupChunker;
        private readonly ILogger<MergeBlockProvider> _logger;
        private readonly IHashValueProvider _hvp;
        private readonly bool _keepPointers;

        public ActionBlock<(PointerFile[], ChunkFile[], ChunkFile[])> GetBlock()
        {
            return new(item =>
            {
                (PointerFile[] pointersToRestore, ChunkFile[] withChunks, ChunkFile[] chunksThatCanBeDeleted) = item;

                _logger.LogInformation($"Merging {withChunks.Length} Chunk(s) into {pointersToRestore.Length} Pointer(s)");

                var target = GetBinaryFileInfo(pointersToRestore.First());
                var bf = Merge(withChunks, target);

                // Verify hash
                var h = _hvp.GetHashValue(bf);

                if (h != pointersToRestore.First().Hash)
                    throw new InvalidDataException("Hash of restored BinaryFile does not match hash of PointerFile");

                // Delete chunks
                chunksThatCanBeDeleted.AsParallel().ForAll(c => c.Delete());

                //Copy to other pointerfiles and set DateTime
                for (int i = 0; i < pointersToRestore.Length; i++)
                {
                    var pfe = pointersToRestore[i];
                    FileInfo fi;

                    if (i == 0)
                    {
                        fi = target;
                    }
                    else
                    {
                        fi = GetBinaryFileInfo(pfe);

                        target.CopyTo(fi.FullName);
                    }

                    fi.CreationTimeUtc = File.GetCreationTimeUtc(pfe.FullName);
                    fi.LastWriteTimeUtc = File.GetLastWriteTimeUtc(pfe.FullName);
                }

                // Delete Pointers
                if (!_keepPointers)
                    pointersToRestore.AsParallel().ForAll(pf => pf.Delete());
            });
        }

        private BinaryFile Merge(IChunkFile[] chunks, FileInfo target)
        {
            if (chunks.Length == 1)
                return _chunker.Merge(chunks, target);
            else
                return _dedupChunker.Merge(chunks, target);
        }

        private static FileInfo GetBinaryFileInfo(PointerFile pf)
        {
            return new FileInfo(pf.FullName.TrimEnd(PointerFile.Extension));
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

