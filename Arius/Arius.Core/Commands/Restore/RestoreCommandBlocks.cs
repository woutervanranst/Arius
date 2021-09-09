using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Arius.Core.Services.Chunkers;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands.Restore
{
    internal class IndexBlock : TaskBlockBase<FileSystemInfo>
    {
        public IndexBlock(ILoggerFactory loggerFactory,
            Func<FileSystemInfo> sourceFunc,
            int maxDegreeOfParallelism,
            bool synchronize,
            Repository repo,
            PointerService pointerService,
            Func<(PointerFile PointerFile, BinaryFile RestoredBinaryFile), Task> indexedPointerFile,
            Action done)
            : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, done: done)
        {
            this.synchronize = synchronize;
            this.repo = repo;
            this.maxDegreeOfParallelism = maxDegreeOfParallelism;
            this.pointerService = pointerService;
            this.indexedPointerFile = indexedPointerFile;
        }

        private readonly bool synchronize;
        private readonly Repository repo;
        private readonly int maxDegreeOfParallelism;
        private readonly PointerService pointerService;
        private readonly Func<(PointerFile PointerFile, BinaryFile RestoredBinaryFile), Task> indexedPointerFile;

        protected override async Task TaskBodyImplAsync(FileSystemInfo fsi)
        {
            if (synchronize)
            {
                if (fsi is DirectoryInfo root)
                {
                    // Synchronize a Directory
                    var currentPfes = (await repo.GetCurrentEntries(includeDeleted: false)).ToArray();

                    logger.LogInformation($"{currentPfes.Length} files in latest version of remote");

                    var t1 = Task.Run(async () => await CreatePointerFilesIfNotExist(root, currentPfes));
                    var t2 = Task.Run(() => DeletePointerFilesIfShouldNotExist(root, currentPfes));

                    await Task.WhenAll(t1, t2);
                }
                else if (fsi is FileInfo)
                    throw new InvalidOperationException($"The synchronize flag is not valid when the path is a file"); //TODO UNIT TEST
                else
                    throw new InvalidOperationException($"The synchronize flag is not valid with argument {fsi}");
            }
            else
            {
                if (fsi is DirectoryInfo root)
                    await ProcessPointersInDirectory(root);
                else if (fsi is FileInfo fi && fi.IsPointerFile())
                    await IndexedPointerFile(new PointerFile(fi.Directory, fi)); //TODO test dit in non root
                else
                    throw new InvalidOperationException($"Argument {fsi} is not valid");
            }
        }

        /// <summary>
        /// Get the PointerFiles for the given PointerFileEntries. Create PointerFiles if they do not exist.
        /// </summary>
        /// <returns></returns>
        private async Task CreatePointerFilesIfNotExist(DirectoryInfo root, PointerFileEntry[] pfes)
        {
            foreach (var pfe in pfes.AsParallel()
                                    .WithDegreeOfParallelism(maxDegreeOfParallelism))
            {
                var pf = pointerService.CreatePointerFileIfNotExists(root, pfe);
                await IndexedPointerFile(pf);
            }
        }

        /// <summary>
        /// Delete the PointerFiles that do not exist in the given PointerFileEntries.
        /// </summary>
        /// <param name="pfes"></param>
        private void DeletePointerFilesIfShouldNotExist(DirectoryInfo root, PointerFileEntry[] pfes)
        {
            var relativeNames = pfes.Select(pfe => pfe.RelativeName).ToArray();

            foreach (var pfi in root.GetPointerFileInfos()
                                        .AsParallel()
                                        .WithDegreeOfParallelism(maxDegreeOfParallelism))
            {
                var relativeName = pfi.GetRelativeName(root);

                if (relativeNames.Contains(relativeName))
                    return;

                pfi.Delete();
                logger.LogInformation($"Pointer for '{relativeName}' deleted");
            }

            root.DeleteEmptySubdirectories();
        }

        private async Task ProcessPointersInDirectory(DirectoryInfo root)
        {
            var pfs = root.GetPointerFileInfos().Select(fi => new PointerFile(root, fi));

            foreach (var pf in pfs)
                await IndexedPointerFile(pf);
        }

        private async Task IndexedPointerFile(PointerFile pf)
        {
            var bf = pointerService.GetBinaryFile(pf, ensureCorrectHash: true);

            await indexedPointerFile((pf, bf)); //bf can be null if it is not yet restored
        }
    }

    internal class DownloadManifestBlock : ChannelTaskBlockBase<ManifestHash>
    {
        public DownloadManifestBlock(ILoggerFactory loggerFactory,
            Func<Channel<ManifestHash>> sourceFunc,
            DirectoryInfo restoreTempDir,
            Repository repo,
            ConcurrentDictionary<ManifestHash, IChunkFile> restoredManifests,
            Action<ManifestHash, IChunk[]> manifestRestored,
            //Action<ManifestHash, ChunkHash[]> setChunksForManifest,
            //Action<ChunkFile> chunkRestored,
            Action done)
            : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, done: done)
        {
            this.restoreTempDir = restoreTempDir;
            this.repo = repo;
            this.restoredManifests = restoredManifests;
            this.manifestRestored = manifestRestored;
            //this.setChunksForManifest = setChunksForManifest;
            //this.chunkRestored = chunkRestored;
        }

        private readonly DirectoryInfo restoreTempDir;
        private readonly Repository repo;
        private readonly Action<ManifestHash, IChunk[]> manifestRestored;
        //private readonly Action<ManifestHash, ChunkHash[]> setChunksForManifest;
        //private readonly Action<ChunkFile> chunkRestored;

        private readonly ConcurrentDictionary<ManifestHash, IChunkFile> restoredManifests;

        private readonly ConcurrentHashSet<ManifestHash> restoringManifests = new();
        private readonly ConcurrentDictionary<ChunkHash, TaskCompletionSource<IChunk>> downloadingChunks = new();

        protected override async Task ForEachBodyImplAsync(ManifestHash mh)
        {
            if (restoredManifests.ContainsKey(mh))
            {
                // the Manifest for this PointerFile is already restored
                throw new NotImplementedException();
                manifestRestored(mh, null);
                return;
            }

            if (!restoringManifests.TryAdd(mh))
                // the Manifest for this PointerFile is already being processed.
                // this method can be called multiple times by S11
                // the waiting PointerFiles will be notified when the first call completes
                return;
            
            var chs = await repo.GetChunkHashesForManifestAsync(mh);
            await Parallel.ForEachAsync(chs,
                new ParallelOptions { MaxDegreeOfParallelism = 1 },
                async (ch, cancellationToken) =>
                {
                    bool toDownload = downloadingChunks.TryAdd(ch, new TaskCompletionSource<IChunk>(TaskCreationOptions.RunContinuationsAsynchronously));
                    if (toDownload)
                    {
                        // this Chunk is not yet downloaded
                        var c = await DownloadChunkAsync(ch);
                        downloadingChunks[ch].SetResult(c);

                        // LOG
                    }
                    else
                    {
                        var t = downloadingChunks[ch].Task;
                        if (!t.IsCompleted)
                        {
                            // the Chunk is being downloaded but not yet completed

                            // LOG

                            await t;
                        }
                        else
                        {
                            // the Chunk is already downloaded

                            // LOG
                        }
                    }
                });

            var cs = await Task.WhenAll(chs.Select(async ch => await downloadingChunks[ch].Task));

            manifestRestored(mh, cs);
        }


        // For unit testing purposes
        internal static bool ChunkRestoredFromLocal { get; set; } = false;
        internal static bool ChunkRestoredFromOnlineTier { get; set; } = false;
        internal static bool Flow4Executed { get; set; } = false;

        private async Task<IChunkFile> DownloadChunkAsync(ChunkHash ch)
        {
            var cfi = GetLocalChunkFileInfo(ch);

            if (cfi.Exists)
            {
                // Downloaded and Decrypted Chunk
                ChunkRestoredFromLocal = true;

                return new ChunkFile(cfi, ch);
            }
            else if (repo.GetChunkBlobByHash(ch, requireHydrated: true) is var cbb && cbb is not null)
            {
                // Hydrated chunk (in cold/hot storage) but not yet downloaded
                ChunkRestoredFromOnlineTier = true;

                await repo.DownloadChunkAsync(cbb, cfi);

                return new ChunkFile(cfi, ch);
            }
            else if (repo.GetChunkBlobByHash(ch, requireHydrated: false) is var cb && cb is not null)
            {
                // Archived chunk (in archive storage) not yet hydrated
                Flow4Executed = true;

                throw new NotImplementedException();

            }
            else
                throw new InvalidOperationException($"Unable to find a chunk '{ch}'");
        }

        private FileInfo GetLocalChunkFileInfo(ChunkHash ch) => new FileInfo(Path.Combine(restoreTempDir.FullName, $"{ch}{ChunkFile.Extension}"));
    }



    internal class RestoreBinaryFileBlock : BlockingCollectionTaskBlockBase<(IChunk[] Chunks, PointerFile[] PointerFiles)>
    {
        public RestoreBinaryFileBlock(ILoggerFactory loggerFactory,
            Func<BlockingCollection<(IChunk[] Chunks, PointerFile[] PointerFiles)>> sourceFunc,
            PointerService pointerService,
            Chunker chunker,
            DirectoryInfo root,
            Action done)
            : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, done: done)
        {
            this.pointerService = pointerService;
            this.chunker = chunker;
            this.root = root;
        }

        private readonly PointerService pointerService;
        private readonly Chunker chunker;
        private readonly DirectoryInfo root;

        protected override async Task ForEachBodyImplAsync((IChunk[] Chunks, PointerFile[] PointerFiles) item)
        {
            FileInfo bfi = null;

            for (int i = 0; i < item.PointerFiles.Length; i++)
            {
                var pf = item.PointerFiles[i];
                FileInfo target;

                if (i == 0)
                {
                    bfi = pointerService.GetBinaryFileInfo(pf);
                    target = bfi;

                    if (bfi.Exists)
                        throw new Exception();

                    await chunker.MergeAsync(root, item.Chunks, bfi);
                }
                else
                {
                    target = pointerService.GetBinaryFileInfo(pf);

                    bfi.CopyTo(target.FullName);
                }

                target.CreationTimeUtc = File.GetCreationTimeUtc(pf.FullName);
                target.LastWriteTimeUtc = File.GetLastWriteTimeUtc(pf.FullName);
            }


            //TODO QUID DELET ECHUNKS


        }

    }
}

