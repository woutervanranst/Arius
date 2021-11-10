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
using ConcurrentCollections;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands.Restore;

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
        : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, onCompleted: done)
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
                var currentPfes = (await repo.PointerFileEntries.GetCurrentEntries(includeDeleted: false)).ToArray();

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
                await IndexedPointerFile(pointerService.GetPointerFile(fi.Directory, fi)); //TODO test dit in non root
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
        var pfs = root.GetPointerFileInfos().Select(fi => pointerService.GetPointerFile(root, fi));

        foreach (var pf in pfs)
            await IndexedPointerFile(pf);
    }

    private async Task IndexedPointerFile(PointerFile pf)
    {
        var bf = pointerService.GetBinaryFile(pf, ensureCorrectHash: true);

        await indexedPointerFile((pf, bf)); //bf can be null if it is not yet restored
    }
}

internal class DownloadChunksForBinaryBlock : ChannelTaskBlockBase<BinaryHash>
{
    public DownloadChunksForBinaryBlock(ILoggerFactory loggerFactory,
        Func<ChannelReader<BinaryHash>> sourceFunc,
        DirectoryInfo restoreTempDir,
        Repository repo,
        ConcurrentDictionary<BinaryHash, IChunkFile> restoredBinaries,
        Func<BinaryHash, IChunk[], Task> chunksRestored,
        Action<BinaryHash> chunksHydrating,
        Action done)
        : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, onCompleted: done)
    {
        this.restoreTempDir = restoreTempDir;
        this.repo = repo;
        this.restoredBinaries = restoredBinaries;
        this.chunksRestored = chunksRestored;
        this.chunksHydrating = chunksHydrating;
    }

    private readonly DirectoryInfo restoreTempDir;
    private readonly Repository repo;
    private readonly Func<BinaryHash, IChunk[], Task> chunksRestored;
    private readonly Action<BinaryHash> chunksHydrating;
    private readonly ConcurrentDictionary<BinaryHash, IChunkFile> restoredBinaries;

    private readonly ConcurrentHashSet<BinaryHash> restoringBinaries = new();
    private readonly ConcurrentDictionary<ChunkHash, TaskCompletionSource<IChunk>> downloadingChunks = new();

    protected override async Task ForEachBodyImplAsync(BinaryHash bh, CancellationToken ct)
    {
        if (restoredBinaries.ContainsKey(bh))
        {
            // the Binary for this PointerFile is already restored
            throw new NotImplementedException();
            await chunksRestored(bh, null);
            return;
        }

        if (!restoringBinaries.Add(bh))
            // the Binary for this PointerFile is already being processed.
            // this method can be called multiple times by S11
            // the waiting PointerFiles will be notified when the first call completes
            return;

        var chs = await repo.Binaries.GetChunkHashesAsync(bh);
        await Parallel.ForEachAsync(chs,
            new ParallelOptions { MaxDegreeOfParallelism = 1 },
            async (ch, cancellationToken) =>
            {
                bool toDownload = downloadingChunks.TryAdd(ch, new TaskCompletionSource<IChunk>(TaskCreationOptions.RunContinuationsAsynchronously));
                if (toDownload)
                {
                        // this Chunk is not yet downloaded
                        var c = await GetChunkFileAsync(ch);
                    downloadingChunks[ch].SetResult(c);
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
        if (cs.Any(c => c is null))
        {
            logger.LogInformation($"At least one Chunk is still hydrating for binary {bh.ToShortString()}... cannot yet restore");
            chunksHydrating(bh);
        }
        else
        {
            logger.LogInformation($"All chunks downloaded for binary {bh.ToShortString()}... ready to restore BinaryFile");
            chunksRestored(bh, cs);
        }
    }


    // For unit testing purposes
    internal static bool ChunkRestoredFromLocal { get; set; } = false;
    internal static bool ChunkRestoredFromOnlineTier { get; set; } = false;
    internal static bool ChunkStartedHydration { get; set; } = false;

    /// <summary>
    /// Get a local ChunkFile for the given ChunkHash
    /// Returns null if it cannot be downloaded because it is not yet hydrated
    /// Throws InvalidOperationException if the cunk cannot be found 
    /// </summary>
    /// <param name="ch"></param>
    /// <returns></returns>
    private async Task<IChunkFile> GetChunkFileAsync(ChunkHash ch)
    {
        var cfi = GetLocalChunkFileInfo(ch);

        if (cfi.Exists)
        {
            // Chunk already downloaded
            ChunkRestoredFromLocal = true;
            logger.LogInformation($"Chunk {ch.ToShortString()} already downloaded");

            return new ChunkFile(cfi, ch);
        }
        else if (repo.Chunks.GetChunkBlobByHash(ch, requireHydrated: true) is var onlineChunk && onlineChunk is not null)
        {
            // Hydrated chunk (in cold/hot storage) but not yet downloaded
            await repo.Chunks.DownloadAsync(onlineChunk, cfi);

            throw new NotImplementedException("check downloaded chunk size with the db");

            ChunkRestoredFromOnlineTier = true;
            logger.LogInformation($"Chunk {ch.ToShortString()} downloaded from online tier");

            return new ChunkFile(cfi, ch);
        }
        else if (repo.Chunks.GetChunkBlobByHash(ch, requireHydrated: false) is var archivedChunk && archivedChunk is not null)
        {
            // Archived chunk (in archive storage) not yet hydrated
            await repo.Chunks.HydrateAsync(archivedChunk);

            ChunkStartedHydration = true;
            logger.LogInformation($"Chunk {ch.ToShortString()} started hydration... cannot yet download");

            return null;
        }
        else
            throw new InvalidOperationException($"Unable to find Chunk '{ch}'");
    }

    private FileInfo GetLocalChunkFileInfo(ChunkHash ch) => new(Path.Combine(restoreTempDir.FullName, $"{ch}{ChunkFile.Extension}"));
}



internal class RestoreBinaryFileBlock : ChannelTaskBlockBase<(IChunk[] Chunks, PointerFile[] PointerFiles)>
{
    public RestoreBinaryFileBlock(ILoggerFactory loggerFactory,
        Func<ChannelReader<(IChunk[] Chunks, PointerFile[] PointerFiles)>> sourceFunc,
        PointerService pointerService,
        Chunker chunker,
        DirectoryInfo root,
        Action done)
        : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, onCompleted: done)
    {
        this.pointerService = pointerService;
        this.chunker = chunker;
        this.root = root;
    }

    private readonly PointerService pointerService;
    private readonly Chunker chunker;
    private readonly DirectoryInfo root;

    protected override async Task ForEachBodyImplAsync((IChunk[] Chunks, PointerFile[] PointerFiles) item, CancellationToken ct)
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
                throw new NotImplementedException(); // todo write unit tests

                target = pointerService.GetBinaryFileInfo(pf);

                bfi.CopyTo(target.FullName);
            }

            target.CreationTimeUtc = File.GetCreationTimeUtc(pf.FullName);
            target.LastWriteTimeUtc = File.GetLastWriteTimeUtc(pf.FullName);
        }


        //TODO QUID DELET ECHUNKS


    }

}

