using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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

    internal class ProcessManifestBlock : BlockingCollectionTaskBlockBase<ManifestHash>
    {
        public ProcessManifestBlock(ILoggerFactory loggerFactory,
            Func<BlockingCollection<ManifestHash>> sourceFunc,
            DirectoryInfo restoreTempDir,
            Repository repo,
            ConcurrentDictionary<ManifestHash, IChunkFile> restoredManifests,
            Action<ManifestHash> manifestRestored,
            Action<ManifestHash, ChunkHash[]> setChunksForManifest,
            Action<ChunkFile> chunkRestored,
            Action done)
            : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, done: done)
        {
            this.restoreTempDir = restoreTempDir;
            this.repo = repo;
            this.restoredManifests = restoredManifests;
            this.manifestRestored = manifestRestored;
            this.setChunksForManifest = setChunksForManifest;
            this.chunkRestored = chunkRestored;
        }

        private readonly DirectoryInfo restoreTempDir;
        private readonly Repository repo;
        private readonly Action<ManifestHash> manifestRestored;
        private readonly Action<ManifestHash, ChunkHash[]> setChunksForManifest;
        private readonly Action<ChunkFile> chunkRestored;

        private readonly ConcurrentDictionary<ManifestHash, IChunkFile> restoredManifests;

        private readonly ConcurrentHashSet<ManifestHash> restoringManifests = new();
        private readonly ConcurrentHashSet<ChunkHash> restoringChunks = new();



        protected override async Task ForEachBodyImplAsync(ManifestHash mh)
        {
            if (restoredManifests.ContainsKey(mh))
            {
                // the Manifest for this PointerFile is already restored
                manifestRestored(mh);
                return;
            }

            if (!restoringManifests.TryAdd(mh))
                // the Manifest for this PointerFile is already being processed
                return;

            var chs = await repo.GetChunkHashesForManifestAsync(mh);
            setChunksForManifest(mh, chs);

            foreach (var ch in chs)
            {
                if (!restoringChunks.TryAdd(ch))
                    // the Chunk for this Manifest is already being processed
                    continue;

                ProcessChunk(ch);
            }
        }

        // For unit testing purposes
        internal static bool chunkRestoredFromLocal = false;
        internal static bool flow2Executed = false;
        internal static bool flow3Executed = false;
        internal static bool flow4Executed = false;

        private void ProcessChunk(ChunkHash ch)
        {
            if (GetLocalChunkFileInfo(ch) is var cfi && cfi.Exists)
            {
                // Downloaded and Decrypted Chunk
                chunkRestoredFromLocal = true;

                var cf = new ChunkFile(cfi, ch);

                chunkRestored(cf);
            }
            else if (GetLocalEncryptedChunkFileInfo(ch) is var ecfi && ecfi.Exists)
            {
                // Downloaded but not yet decrypted chunk
                flow2Executed = true;

            }
            else if (repo.GetChunkBlobByHash(ch, requireHydrated: true) is var hcb && hcb is not null)
            {
                // Hydrated chunk (in cold/hot storage) but not yet downloaded
                flow3Executed = true;
            }
            else if (repo.GetChunkBlobByHash(ch, requireHydrated: false) is var cb && cb is not null)
            {
                // Archived chunk (in archive storage) not yet hydrated
                flow4Executed = true;

            }
            else
                throw new InvalidOperationException($"Unable to find a chunk '{ch}'");
        }

        private FileInfo GetLocalChunkFileInfo(ChunkHash ch) => new FileInfo(Path.Combine(restoreTempDir.FullName, $"{ch}{ChunkFile.Extension}"));
        private FileInfo GetLocalEncryptedChunkFileInfo(ChunkHash ch) => new FileInfo(Path.Combine(restoreTempDir.FullName, $"{ch}{EncryptedChunkFile.Extension}"));
    }






    //internal class RestoreManifestBlock : BlockingCollectionTaskBlockBase<(ManifestHash ManifestHash, ChunkHash[] ChunkHashes)>
    //{
    //    public RestoreManifestBlock(ILogger<RestoreManifestBlock> logger,
    //        Func<BlockingCollection<(ManifestHash ManifestHash, ChunkHash[] ChunkHashes)>> sourceFunc,
    //        DirectoryInfo restoreTempDir,
    //        //Repository repo,
    //        //ConcurrentDictionary<ManifestHash, FileInfo> restoredManifests,
    //        //Action<ManifestHash> manifestRestored,
    //        //Action<ManifestHash, ChunkHash[]> chunksForManifest,
    //        Action<FileInfo> manifestRestored,
    //        Action done)
    //        : base(logger: logger, sourceFunc: sourceFunc, done: done)
    //    {
    //        this.restoreTempDir = restoreTempDir;
    //        this.manifestRestored = manifestRestored;
    //    }

    //    private readonly DirectoryInfo restoreTempDir;
    //    private readonly Action<FileInfo> manifestRestored;

    //    protected override Task ForEachBodyImplAsync((ManifestHash ManifestHash, ChunkHash[] ChunkHashes) item)
    //    {
    //        throw new NotImplementedException();
    //    }
    //}

    internal class RestorePointerFileBlock : BlockingCollectionTaskBlockBase<(IChunkFile[] ChunkFiles, PointerFile[] PointerFiles)>
    {
        private readonly PointerService pointerService;
        private readonly Chunker chunker;

        public RestorePointerFileBlock(ILoggerFactory loggerFactory,
            Func<BlockingCollection<(IChunkFile[] ChunkFiles, PointerFile[] PointerFiles)>> sourceFunc,
            //ConcurrentDictionary<ManifestHash, FileInfo> restoredManifests,
            //Action<PointerFile, BinaryFile> manifestRestored,
            //Action<ManifestHash, ChunkHash[]> chunksForManifest,
            PointerService pointerService,
            Chunker chunker,
            Action done)
            : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, done: done)
        {
            this.pointerService = pointerService;
            this.chunker = chunker;
            //this.restoredManifests = restoredManifests;
        }

        //private readonly ConcurrentDictionary<ManifestHash, FileInfo> restoredManifests;


        protected override Task ForEachBodyImplAsync((IChunkFile[] ChunkFiles, PointerFile[] PointerFiles) item)
        {
            // Restore 

            if (item.PointerFiles.Length == 1)
            {
                var pf = item.PointerFiles.Single();
                var target = pointerService.GetBinaryFileInfo(pf);

                

                if (target.Exists)
                    throw new Exception();

                throw new NotImplementedException(); 

                //chunker.Merge(null, item.ChunkFiles, target);

                //item.Binary.MoveTo(target.FullName);

                target.CreationTimeUtc = File.GetCreationTimeUtc(pf.FullName);
                target.LastWriteTimeUtc = File.GetLastWriteTimeUtc(pf.FullName);
            }
            else
            {
                //More than one to restore
                throw new NotImplementedException();
            }


            //TODO QUID DELET ECHUNKS



            return Task.CompletedTask;


        }

    }
}

