using System;
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

    internal class DiscardDownloadedPointerFilesBlockProvider
    {
        private readonly ILogger<DiscardDownloadedPointerFilesBlockProvider> _logger;
        private readonly IHashValueProvider _hvp;

        public DiscardDownloadedPointerFilesBlockProvider(ILogger<DiscardDownloadedPointerFilesBlockProvider> logger, IHashValueProvider hvp)
        {
            _logger = logger;
            _hvp = hvp;
        }

        public TransformManyBlock<PointerFile, PointerFile> GetBlock()
        {
            return new(pf =>
            {
                if (pf.BinaryFileInfo is var bfi && bfi.Exists &&
                    new BinaryFile(pf.Root, bfi) is var bf && _hvp.GetHashValue(bf).Equals(pf.Hash))
                {
                    _logger.LogInformation($"PointerFile {pf.RelativeName} already downloaded - skipping");

                    return Enumerable.Empty<PointerFile>(); //This file is already restored -- skip
                }

                //TODO TEST: binary file already exist - do not 

                return new[] {pf};
            });
        }
    }

    internal class ChunkDownloadQueueBlockProvider
    {
        public ChunkDownloadQueueBlockProvider(IConfiguration config, AzureRepository repo)
        {
            _config = config;
            _repo = repo;
        }

        public ChunkDownloadQueueBlockProvider AddSourceBlock(ISourceBlock<PointerFile> source)
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

        public TransformManyBlock<PointerFile, RemoteEncryptedChunkBlobItem[]> GetBlock()
        {
            return new(pf =>
            {
                var chunkHashValues = _repo.GetChunkHashes(pf.Hash);

                lock (_notYetDownloading)
                {
                    lock (_downloadedOrDownloading)
                    {
                        foreach (var chunkHash in chunkHashValues)
                        {
                            if (!(_notYetDownloading.ContainsKey(chunkHash) || _downloadedOrDownloading.Contains(chunkHash)))
                            {
                                var recbi = _repo.GetChunkBlobItemByHash(chunkHash);

                                _notYetDownloading.Add(chunkHash, recbi);
                            }
                        }

                        if (_notYetDownloading.Values.Sum(recbi => recbi.Length) >= _config.BatchSize ||
                            _notYetDownloading.Count >= _config.BatchCount ||
                            _source.Completion.IsCompleted)
                        {
                            //Emit a batch
                            var batch = new[] {_notYetDownloading.Values.ToArray()};

                            _downloadedOrDownloading.AddRange(_notYetDownloading.Keys);
                            _notYetDownloading.Clear();

                            // IF SOURCE COMPLETED + THIS EMPTY SET TO COMPLETE ?

                            return batch;
                        }
                        else
                        {
                            //Wait unil more values accumulate
                            return Enumerable.Empty<RemoteEncryptedChunkBlobItem[]>();
                        }
                    }
                }
            });
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
