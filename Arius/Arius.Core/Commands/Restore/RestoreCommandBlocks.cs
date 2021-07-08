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
        public IndexBlock(ILogger<IndexBlock> logger,
            Func<FileSystemInfo> sourceFunc,
            int maxDegreeOfParallelism,
            bool synchronize,
            Repository repo,
            PointerService pointerService,
            Action<(PointerFile PointerFile, BinaryFile RestoredBinaryFile)> indexedPointerFile,
            Action done)
            : base(logger: logger, sourceFunc: sourceFunc, done: done)
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
        private readonly Action<(PointerFile PointerFile, BinaryFile RestoredBinaryFile)> indexedPointerFile;

        protected override async Task TaskBodyImplAsync(FileSystemInfo fsi)
        {
            if (synchronize)
            {
                if (fsi is DirectoryInfo root)
                {
                    // Synchronize a Directory
                    var currentPfes = (await repo.GetCurrentEntries(includeDeleted: false)).ToArray();

                    logger.LogInformation($"{currentPfes.Length} files in latest version of remote");

                    var t1 = Task.Run(() => CreatePointerFilesIfNotExist(root, currentPfes));
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
                    ProcessPointersInDirectory(root);
                else if (fsi is FileInfo fi && fi.IsPointerFile())
                    IndexedPointerFile(new PointerFile(fi.Directory, fi)); //TODO test dit in non root
                else
                    throw new InvalidOperationException($"Argument {fsi} is not valid");
            }
        }

        /// <summary>
        /// Get the PointerFiles for the given PointerFileEntries. Create PointerFiles if they do not exist.
        /// </summary>
        /// <returns></returns>
        private void CreatePointerFilesIfNotExist(DirectoryInfo root, PointerFileEntry[] pfes)
        {
            foreach (var pfe in pfes.AsParallel()
                                    .WithDegreeOfParallelism(maxDegreeOfParallelism))
            {
                var pf = pointerService.CreatePointerFileIfNotExists(root, pfe);
                IndexedPointerFile(pf);
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

        private void ProcessPointersInDirectory(DirectoryInfo root)
        {
            var pfs = root.GetPointerFileInfos().Select(fi => new PointerFile(root, fi));

            foreach (var pf in pfs)
                IndexedPointerFile(pf);
        }

        private void IndexedPointerFile(PointerFile pf)
        {
            var bf = pointerService.GetBinaryFile(pf, ensureCorrectHash: true);

            indexedPointerFile((pf, bf)); //bf can be null if it is not yet restored
        }
    }

    internal class ProcessPointerFileBlock : BlockingCollectionTaskBlockBase<PointerFile>
    {
        public ProcessPointerFileBlock(ILogger<ProcessPointerFileBlock> logger,
            Func<BlockingCollection<PointerFile>> sourceFunc,
            DirectoryInfo restoreTempDir,
            Repository repo,
            ConcurrentDictionary<ManifestHash, BinaryFile> restoredManifests,
            Action<PointerFile, BinaryFile> manifestRestored,
            Action<ManifestHash, ChunkHash[]> chunksForManifest,
            Action done)
            : base(logger: logger, sourceFunc: sourceFunc, done: done)
        {
            this.restoreTempDir = restoreTempDir;
            this.repo = repo;
            this.restoredManifests = restoredManifests;
            this.manifestRestored = manifestRestored;
            this.chunksForManifest = chunksForManifest;
        }

        private readonly DirectoryInfo restoreTempDir;
        private readonly Repository repo;
        private readonly Action<PointerFile, BinaryFile> manifestRestored;
        private readonly Action<ManifestHash, ChunkHash[]> chunksForManifest;

        private readonly ConcurrentDictionary<ManifestHash, BinaryFile> restoredManifests;

        private readonly ConcurrentHashSet<ManifestHash> restoringManifests = new();
        private readonly ConcurrentHashSet<ChunkHash> restoringChunks = new();



        protected override async Task ForEachBodyImplAsync(PointerFile pf)
        {
            if (restoredManifests.ContainsKey(pf.Hash))
            {
                // the Manifest for this PointerFile is already restored
                manifestRestored(pf, restoredManifests[pf.Hash]);
                return;
            }

            if (!restoringManifests.TryAdd(pf.Hash))
                // the Manifest for this PointerFile is already being processed
                return;

            var chs = await repo.GetChunkHashesForManifestAsync(pf.Hash);
            chunksForManifest(pf.Hash, chs);

            foreach (var ch in chs)
            {
                if (!restoringChunks.TryAdd(ch))
                    // the Chunk for this Manifest is already being processed
                    continue;

                ProcessChunk(ch);
            }
        }

        private void ProcessChunk(ChunkHash ch)
        {
            if (GetLocalChunkFileInfo(ch) is var cfi && cfi.Exists)
            {
                // Downloaded and Decrypted Chunk

            }
            else if (GetLocalEncryptedChunkFileInfo(ch) is var ecfi && ecfi.Exists)
            {
                // Downloaded but not yet decrypted chunk

            }
            else if (repo.GetChunkBlobByHash(ch, requireHydrated: true) is var hcb && hcb is not null)
            {
                // Hydrated chunk (in cold/hot storage) but not yet downloaded
            }
            else if (repo.GetChunkBlobByHash(ch, requireHydrated: false) is var cb && cb is not null)
            {
                // Archived chunk (in archive storage) not yet hydrated

            }
            else
                throw new InvalidOperationException($"Unable to find a chunk '{ch}'");
        }

        private FileInfo GetLocalChunkFileInfo(ChunkHash ch) => new FileInfo(Path.Combine(restoreTempDir.FullName, $"{ch}{ChunkFile.Extension}"));
        private FileInfo GetLocalEncryptedChunkFileInfo(ChunkHash ch) => new FileInfo(Path.Combine(restoreTempDir.FullName, $"{ch}{EncryptedChunkFile.Extension}"));
    }
}

