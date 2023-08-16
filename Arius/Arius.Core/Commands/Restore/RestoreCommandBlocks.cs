using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands.Restore;

internal class IndexBlock : TaskBlockBase<FileSystemInfo>
{
    public IndexBlock(ILoggerFactory loggerFactory,
       Func<FileSystemInfo> sourceFunc,
       int maxDegreeOfParallelism,
       bool synchronize,
       Repository repo,
       FileSystemService fileSystemService,
       FileService fileService,
       Func<PointerFile, Task> onIndexedPointerFile,
       Action onCompleted)
       : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, onCompleted: onCompleted)
    {
        this.maxDegreeOfParallelism = maxDegreeOfParallelism;
        this.synchronize            = synchronize;
        this.repo                   = repo;
        this.fileSystemService      = fileSystemService;
        this.fileService            = fileService;
        this.onIndexedPointerFile   = onIndexedPointerFile;
    }

    private readonly int                     maxDegreeOfParallelism;
    private readonly bool                    synchronize;
    private readonly Repository              repo;
    private readonly FileSystemService       fileSystemService;
    private readonly FileService             fileService;
    private readonly Func<PointerFile, Task> onIndexedPointerFile;

    protected override async Task TaskBodyImplAsync(FileSystemInfo source)
    {
        if (synchronize)
        {
            if (source is not DirectoryInfo di)
                throw new ArgumentException($"The synchronize flag is only valid for directories");

            await SynchronizeThenIndex(di);
        }
        else
        {
            await Index(source);
        }
    }

    private async Task SynchronizeThenIndex(DirectoryInfo root)
    {
        var currentPfes = (await repo.GetCurrentPointerFileEntriesAsync(includeDeleted: false)).ToArray();

        logger.LogInformation($"{currentPfes.Length} files in latest version of remote");

        var t1 = Task.Run(async () => await CreatePointerFilesIfNotExistAsync(root, currentPfes));
        var t2 = Task.Run(() => DeletePointerFilesIfShouldNotExist(root, currentPfes));

        await Task.WhenAll(t1, t2);
    }

    /// <summary>
    /// Get the PointerFiles for the given PointerFileEntries. Create PointerFiles if they do not exist.
    /// </summary>
    /// <returns></returns>
    private async Task CreatePointerFilesIfNotExistAsync(DirectoryInfo root, PointerFileEntry[] pfes)
    {
        await Parallel.ForEachAsync(pfes,
            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
            async (pfe, ct) => 
            {
                var (_, pf) = fileService.CreatePointerFileIfNotExists(root, pfe);
                await onIndexedPointerFile(pf);
            });
    }

    /// <summary>
    /// Delete the PointerFiles that do not exist in the given PointerFileEntries.
    /// </summary>
    /// <param name="pfes"></param>
    private void DeletePointerFilesIfShouldNotExist(DirectoryInfo root, PointerFileEntry[] pfes)
    {
        var relativeNames = pfes.Select(pfe => pfe.RelativeName).ToArray();

        Parallel.ForEach(fileSystemService.GetPointerFileInfos(root),
            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
            (pfi) =>
            {
                var relativeName = pfi.GetRelativeName(root);

                if (relativeNames.Contains(relativeName))
                    return;

                pfi.Delete();
                logger.LogInformation($"Pointer for '{relativeName}' deleted");
            });

        root.DeleteEmptySubdirectories();
    }


    private async Task Index(FileSystemInfo source)
    {
        if (source is DirectoryInfo root)
            await ProcessPointersInDirectory(root);
        else if (source is FileInfo fi && fi.IsPointerFile())
            await onIndexedPointerFile(fileService.GetExistingPointerFile(fi.Directory, FileSystemService.GetPointerFileInfo(fi))); //TODO test dit in non root
        else
            throw new InvalidOperationException($"Argument {source} is not valid");
    }

    private async Task ProcessPointersInDirectory(DirectoryInfo root)
    {
        var pfs = fileSystemService.GetPointerFileInfos(root).Select(pfi => fileService.GetExistingPointerFile(root, pfi));

        foreach (var pf in pfs)
            await onIndexedPointerFile(pf);
    }
}

internal class DownloadBinaryBlock : ChannelTaskBlockBase<PointerFile>
{
    public DownloadBinaryBlock(ILoggerFactory loggerFactory,
        Func<ChannelReader<PointerFile>> sourceFunc,
        FileService fileService,
        Repository repo,
        IRestoreCommandOptions options,
        Action chunkRehydrating,
        Action onCompleted,
        int maxDegreeOfParallelism)
        : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, maxDegreeOfParallelism: maxDegreeOfParallelism, onCompleted: onCompleted)
    {
        this.fileService      = fileService;
        this.repo             = repo;
        this.options          = options;
        this.chunkRehydrating = chunkRehydrating;
    }

    private readonly FileService            fileService;
    private readonly Repository             repo;
    private readonly IRestoreCommandOptions options;
    private readonly Action                 chunkRehydrating;

    private readonly ConcurrentDictionary<BinaryHash, TaskCompletionSource<BinaryFile>> restoredBinaries = new();

    // For unit testing purposes
    internal static bool RestoredFromLocal      { get; set; } = false;
    internal static bool RestoredFromOnlineTier { get; set; } = false;

    protected override async Task ForEachBodyImplAsync(PointerFile pf, CancellationToken ct)
    {
        /* This PointerFile may need to be restored.
         * 
         * 1.   [At the start up the run] The BinaryFile for this PointerFile exists --> no need to restore
         * 2.1. [At the start up the run] The BinaryFile for this PointerFile does not yet exist, and download of the Binary has not started --> start the download
         * 2.2. [At the start up the run] The BinaryFile for this PointerFile does not yet exist, and download of the Binary has started but not completed --> wait for it
         * 2.3. [At the start up the run] The BinaryFile for this PointerFile does not yet exist, and download has completed --> copy
         * 3.   We encounter a restored BinaryFile while we are downloading the same binary?????????????????????????????????????????????????????????
         */

        var binary = await fileService.GetExistingBinaryFileAsync(pf, assertHash: true);
        if (binary is not null)
        {
            // 1. The Binary is already restored
            restoredBinaries.AddOrUpdate(binary.BinaryHash,
                addValueFactory: _ =>
                {
                    logger.LogInformation($"Binary '{binary.RelativeName}' already restored.");
                    
                    var tcs = new TaskCompletionSource<BinaryFile>(TaskCreationOptions.RunContinuationsAsynchronously);
                    tcs.SetResult(binary);

                    return tcs;
                },
                updateValueFactory: (bh, tcs) =>
                {
                    /* We are have already processed the hash for this binaryfile, because
                     * 3.1. This pf is a duplicate of a pf2 that was already restored before the run
                     * 3.2. This pf is a duplicate of a pf2 that WAS restored DURING the run
                     * 3.3. This pf is a duplicate of a pf2 that IS currently BEING restored
                     */

                    if (tcs.Task.IsCompleted)
                    {
                        // 3.1 + 3.2: BinaryFile already restored, no need to update the tcs
                        logger.LogInformation($"No need to restore binary for {pf.RelativeName} ('{pf.BinaryHash.ToShortString()}') is already restored in '{binary.RelativeName}'");
                    }
                    else
                    {
                        // 3.3
                        logger.LogInformation($"Binary for {pf.RelativeName} ('{pf.BinaryHash.ToShortString()}') is being downloaded but we encountered a local duplicate ({binary.RelativeName}). Using that one.");
                        // TODO cancel the ongoing download and use tcs.Task.Result as BinaryFile to copy to pf

                        tcs.SetResult(binary);
                    }

                    return tcs;
                });
        }
        else
        {
            // 2. The Binary is not yet restored

            var binaryToDownload = restoredBinaries.TryAdd(pf.BinaryHash, new TaskCompletionSource<BinaryFile>(TaskCreationOptions.RunContinuationsAsynchronously));
            if (binaryToDownload)
            {
                // 2.1 Download not yet started --> start download
                logger.LogDebug($"Starting download for Binary '{pf.BinaryHash.ToShortString()}' ('{pf.RelativeName}')");

                bool restored;
                try
                {
                    restored = await TryDownloadAsync(pf.BinaryHash, FileSystemService.GetBinaryFileInfo(pf), options);
                }
                catch (Exception e)
                {
                    var e2 = new InvalidOperationException($"Could not restore binary ({pf.BinaryHash}) for {pf.RelativeName}. Delete the PointerFile, disable synchronize and try again to restore without this binary.", e);
                    logger.LogError(e2);

                    throw e2;
                }

                if (restored)
                {
                    RestoredFromOnlineTier = true;
                    binary = await fileService.GetExistingBinaryFileAsync(pf, assertHash: true);
                }
                else
                    chunkRehydrating();

                if (!restoredBinaries[pf.BinaryHash].Task.IsCompleted)
                {
                    restoredBinaries[pf.BinaryHash].SetResult(binary); //also set in case of null (ie not restored because still in Archive tier)
                }
                else
                {
                    // 3.3 while we were downloading this binary, we encountered a local file that had the same hash --> no need to set the binary
                    logger.LogInformation($"We downloaded the binary for {pf.RelativeName} but meanwhile we encountered a local copy already. Download was acutally unnecessary. Sorry.");
                }
            }
            else
            {
                // 2.2 Download ongoing --> wait for it
                binary = await restoredBinaries[pf.BinaryHash].Task;
            }

            if (binary is null)
                //the binary could not yet be restored -- nothing left to do here
                return;

            //TODO what if chunk does not exist?


        }

        var targetBinary = FileSystemService.GetBinaryFileInfo(pf);
        if (!targetBinary.Exists)
        {
            //TODO ensure this path is tested
            
            //The Binary was already restored in another BinaryFile bf (ie this pf is a duplicate) --> copy the bf to this pf
            logger.LogInformation($"Restoring '{pf.RelativeName}' '({pf.BinaryHash.ToShortString()})' from '{binary.RelativeName}' to '{targetBinary.FullName}'");
            RestoredFromLocal = true;

            await using (var ss = await binary.OpenReadAsync())
            {
                targetBinary.Directory.Create();
                await using var ts = File.OpenWrite(targetBinary.FullName);
                await ss.CopyToAsync(ts); // File.Copy keeps the file locked when we re setting CreationTime and LastWriteTime
            }
        }

        targetBinary.CreationTimeUtc = File.GetCreationTimeUtc(pf.FullName);
        targetBinary.LastWriteTimeUtc = File.GetLastWriteTimeUtc(pf.FullName);

        if (!options.KeepPointers)
            pf.Delete();
    }

    /// <summary>
    /// Download the given Binary with the specified options.
    /// Start hydration for the chunks if required.
    /// Returns null if the Binary is not yet hydrated
    /// </summary>
    private async Task<bool> TryDownloadAsync(BinaryHash bh, BinaryFileInfo target, IRestoreCommandOptions options, bool rehydrateIfNeeded = true)
    {

        //var chunks = await repo.GetChunkListAsync(bh)
        //    .SelectAwait(async ch =>
        //    {
        //        var hcb = await repo.GetHydratedChunkBlobAsync(ch);

        //        return new
        //        {
        //            ChunkHash         = ch,
        //            HydratedChunkBlob = hcb,
        //            ArchivedChunkBlob = hcb == null ? await repo.GetChunkBlobAsync(ch) : null
        //        };
        //    })
        //    .ToArrayAsync();


        var chunks = await repo.GetChunkListAsync(bh)
            .SelectAwait(async ch => (ChunkHash: ch, HydratedChunkBlob: await repo.GetHydratedChunkBlobAsync(ch)))
            .ToArrayAsync();

        var chunksToHydrate = chunks
            .Where(c => c.HydratedChunkBlob is null);
            //.Select(c => repo.GetChunkBlob(c.ChunkHash));
        if (chunksToHydrate.Any())
        {
            chunksToHydrate = chunksToHydrate.ToArray();
            //At least one chunk is not hydrated so the Binary cannot be downloaded
            logger.LogInformation($"{chunksToHydrate.Count()} chunk(s) for '{bh.ToShortString()}' not hydrated. Cannot yet restore.");

            if (rehydrateIfNeeded)
                foreach (var c in chunksToHydrate)
                    //hydrate this chunk
                    await repo.HydrateChunkAsync(c.ChunkHash);

            return false;
        }
        else
        {
            //All chunks are hydrated  so we can restore the Binary
            logger.LogInformation($"Downloading Binary '{bh.ToShortString()}' from {chunks.Length} chunk(s)...");

            var p = await repo.GetBinaryPropertiesAsync(bh);
            var stats = await new Stopwatch().GetSpeedAsync(p.ArchivedLength, async () =>
            {
                await using var ts = target.OpenWriteAsync();

                /* Faster version but more code
                //if (chunks.Length == 1)
                //{
                //    await using var cs = await chunks[0].ChunkBlob.OpenReadAsync();
                //    await CryptoService.DecryptAndDecompressAsync(cs, ts, options.Passphrase);
                //}
                //else
                //{
                //    var x = new ConcurrentDictionary<ChunkHash, byte[]>();

                //    var t0 = Task.Run(async () =>
                //    {
                //        await Parallel.ForEachAsync(chunks,
                //            new ParallelOptions() { MaxDegreeOfParallelism = 20 },
                //            async (c, ct) =>
                //            {
                //                await using var ms = new MemoryStream();
                //                await using var cs = await c.ChunkBlob.OpenReadAsync();
                //                await CryptoService.DecryptAndDecompressAsync(cs, ms, options.Passphrase);
                //                if (!x.TryAdd(c.ChunkHash, ms.ToArray()))
                //                    throw new InvalidOperationException();
                //            });
                //    });

                //    var t1 = Task.Run(async () =>
                //    {
                //        foreach (var (ch, _) in chunks)
                //        {
                //            while (!x.ContainsKey(ch))
                //                await Task.Yield();

                //            if (!x.TryRemove(ch, out var buff))
                //                throw new InvalidOperationException();

                //            await ts.WriteAsync(buff);
                //            //await x[ch].CopyToAsync(ts);
                //        }
                //    });

                //    Task.WaitAll(t0, t1);
                //}
                */

                foreach (var (_, cb) in chunks)
                {
                    await using var cs = await cb!.OpenReadAsync();
                    await CryptoService.DecryptAndDecompressAsync(cs, ts, options.Passphrase);
                }
            });

            logger.LogInformation($"Downloading Binary '{bh.ToShortString()}' of {p.ArchivedLength.GetBytesReadable()} from {chunks.Length} chunk(s)... Completed in {stats.seconds}s ({stats.MBps} MBps / {stats.Mbps} Mbps)");

            return true;
        }
    }


}