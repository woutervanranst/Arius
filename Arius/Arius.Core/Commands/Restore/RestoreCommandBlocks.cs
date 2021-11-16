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
       Func<PointerFile, Task> onIndexedPointerFile,
       Action onCompleted)
       : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, onCompleted: onCompleted)
    {
        this.maxDegreeOfParallelism = maxDegreeOfParallelism;
        this.synchronize = synchronize;
        this.repo = repo;
        this.pointerService = pointerService;
        this.onIndexedPointerFile = onIndexedPointerFile;
    }

    private readonly int maxDegreeOfParallelism;
    private readonly bool synchronize;
    private readonly Repository repo;
    private readonly PointerService pointerService;
    private readonly Func<PointerFile, Task> onIndexedPointerFile;

    protected override async Task TaskBodyImplAsync(FileSystemInfo source)
    {
        if (synchronize)
        {
            if (source is not DirectoryInfo)
                throw new ArgumentException($"The synchronize flag is only valid for directories");

            await SynchronizeThenIndex((DirectoryInfo)source);
        }
        else
        {
            await Index(source);
        }
    }

    private async Task SynchronizeThenIndex(DirectoryInfo root)
    {
        var currentPfes = (await repo.PointerFileEntries.GetCurrentEntries(includeDeleted: false)).ToArray();

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
                var pf = pointerService.CreatePointerFileIfNotExists(root, pfe);
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

        Parallel.ForEach(root.GetPointerFileInfos(),
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
            await onIndexedPointerFile(pointerService.GetPointerFile(fi.Directory, fi)); //TODO test dit in non root
        else
            throw new InvalidOperationException($"Argument {source} is not valid");
    }

    private async Task ProcessPointersInDirectory(DirectoryInfo root)
    {
        var pfs = root.GetPointerFileInfos().Select(fi => pointerService.GetPointerFile(root, fi));

        foreach (var pf in pfs)
            await onIndexedPointerFile(pf);
    }
}

internal class DownloadBinaryBlock : ChannelTaskBlockBase<PointerFile>
{
    public DownloadBinaryBlock(ILoggerFactory loggerFactory,
        Func<ChannelReader<PointerFile>> sourceFunc,
        PointerService pointerService,
        Repository repo,
        RestoreCommandOptions options,
        Action chunkRehydrating,
        Action onCompleted,
        int maxDegreeOfParallelism)
        : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, maxDegreeOfParallelism: maxDegreeOfParallelism, onCompleted: onCompleted)
    {
        this.pointerService = pointerService;
        this.repo = repo;
        this.options = options;
        this.chunkRehydrating = chunkRehydrating;
    }

    private readonly ConcurrentDictionary<BinaryHash, TaskCompletionSource<BinaryFile>> restoredBinaries = new();
    private readonly PointerService pointerService;
    private readonly Repository repo;
    private readonly RestoreCommandOptions options;
    private readonly Action chunkRehydrating;

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

        var bf = pointerService.GetBinaryFile(pf, ensureCorrectHash: true);
        if (bf is not null)
        {
            // 1. The Binary is already restored
            restoredBinaries.AddOrUpdate(bf.Hash,
                addValueFactory: (_) =>
                {
                    logger.LogInformation($"Binary '{bf.RelativeName}' already restored.");
                    var tcs = new TaskCompletionSource<BinaryFile>(TaskCreationOptions.RunContinuationsAsynchronously);
                    tcs.SetResult(bf);
                    return tcs;
                },
                updateValueFactory: (bh, tcs) =>
                {
                    if (tcs.Task.IsCompleted)
                        return tcs; // this is a duplicate binary

                    tcs.SetResult(bf);
                    return tcs;
                });
        }
        else
        {
            // 2. The Binary is not yet restored

            var binaryToDownload = restoredBinaries.TryAdd(pf.Hash, new TaskCompletionSource<BinaryFile>(TaskCreationOptions.RunContinuationsAsynchronously));
            if (binaryToDownload)
            {
                // 2.1 Download not yet started --> start download
                logger.LogInformation($"Starting download for Binary '{pf.Hash.ToShortString()}' ('{pf.RelativeName}')");

                bool restored;
                try
                {
                    restored = await repo.Binaries.TryDownloadAsync(pf.Hash, pointerService.GetBinaryFileInfo(pf), options);
                }
                catch (Exception e)
                {
                    var e2 = new InvalidOperationException($"Could not restore binary ({pf.Hash}) for {pf.RelativeName}. Delete the PointerFile, disable synchronize and try again to restore without this binary.", e);
                    logger.LogError(e2);

                    throw e2;
                }

                if (restored)
                    bf = pointerService.GetBinaryFile(pf, ensureCorrectHash: true);

                restoredBinaries[pf.Hash].SetResult(bf); //also set in case of null (ie not restored)
            }
            else
            {
                // 2.2 Download ongoing --> wait for it
                bf = await restoredBinaries[pf.Hash].Task;
            }

            if (bf is null)
                //the binary could not yet be restored -- nothing left to do here
                return;

            //TODO what if chunk does not exist?

            //// For unit testing purposes
            //internal static bool ChunkRestoredFromLocal { get; set; } = false;
            //internal static bool ChunkRestoredFromOnlineTier { get; set; } = false;
            //internal static bool ChunkStartedHydration { get; set; } = false;
        }

        var bfi = pointerService.GetBinaryFileInfo(pf);
        if (!bfi.Exists)
        {
            File.Copy(bf.FullName, bfi.FullName);
        }

        bfi.CreationTimeUtc = File.GetCreationTimeUtc(pf.FullName);
        bfi.LastWriteTimeUtc = File.GetLastWriteTimeUtc(pf.FullName);
    }
}