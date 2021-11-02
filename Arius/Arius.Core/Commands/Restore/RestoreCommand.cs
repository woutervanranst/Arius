using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Arius.Core.Services.Chunkers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands.Restore;

internal class RestoreCommand : ICommand //This class is internal but the interface is public for use in the Facade
{
    public RestoreCommand(RestoreCommandOptions options,
        ILogger<RestoreCommand> logger,
        IServiceProvider serviceProvider)
    {
        this.options = options;
        this.logger = logger;
        services = serviceProvider;
    }

    private readonly RestoreCommandOptions options;
    private readonly ILogger<RestoreCommand> logger;
    private readonly IServiceProvider services;

    IServiceProvider ICommand.Services => services;

    public async Task<int> Execute()
    {
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var repo = services.GetRequiredService<Repository>();
        var pointerService = services.GetRequiredService<PointerService>();

        DirectoryInfo root;
        FileSystemInfo pathToRestore;

        if (File.GetAttributes(options.Path).HasFlag(FileAttributes.Directory))
        {
            root = new DirectoryInfo(options.Path);
            pathToRestore = root;
        }
        else
        {
            var file = new FileInfo(options.Path);
            root = file.Directory;
            pathToRestore = file;
        }

            
        logger.LogInformation("Determining PointerFiles to restore...");


        var binariesToDownload = Channel.CreateUnbounded<BinaryHash>();
        var restoredBinaries = new ConcurrentDictionary<BinaryHash, IChunkFile>();
        var pointerFilesWaitingForBinaryRestoration = new ConcurrentDictionary<BinaryHash, ConcurrentBag<PointerFile>>(); //Key: BinaryHash. Values (PointerFiles) that are waiting for the Keys (Binaries) to be created

        var indexBlock = new IndexBlock(
            loggerFactory: loggerFactory,
            sourceFunc: () => pathToRestore, //S10
            maxDegreeOfParallelism: 2,
            synchronize: options.Synchronize,
            repo: repo,
            pointerService: pointerService,
            indexedPointerFile: async arg =>
            {
                if (!options.Download)
                    return; //no need to download

                var (pf, bf) = arg;
                if (bf is null)
                {
                    // need to download the binary for this pointer
                    await binariesToDownload.Writer.WriteAsync(pf.Hash); //S11
                    pointerFilesWaitingForBinaryRestoration.AddOrUpdate( //S14
                        key: pf.Hash,
                        addValue: new() { pf },
                        updateValueFactory: (h, bag) =>
                        {
                            bag.Add(pf);
                            return bag;
                        });
                }
                if (bf is not null)
                { 
                    // this binary / binaryfile is already restored
                    restoredBinaries.TryAdd(bf.Hash, bf); //S12 //NOTE: TryAdd returns false if this key is already present but that is OK, we just need a single BinaryFile to be present in order to restore future potential duplicates
                }
            },
            done: () => 
            {
                binariesToDownload.Writer.Complete(); //S13
            });
        var indexTask = indexBlock.GetTask;

        await indexTask; //S19

        logger.LogInformation($"Determining PointerFiles to restore... done. {binariesToDownload.Reader.Count} PointerFile(s) to restore.");


        var chunksForBinary = new ConcurrentDictionary<BinaryHash, (ChunkHash[] All, List<ChunkHash> PendingDownload)>();
        var pointersToRestore = Channel.CreateBounded<(IChunk[] Chunks, PointerFile[] PointerFiles)>(new BoundedChannelOptions(options.PARALLELISLD) { AllowSynchronousContinuations = false, FullMode = BoundedChannelFullMode.Wait, SingleReader = false, SingleWriter = false});
        var downloadedChunks = new ConcurrentDictionary<ChunkHash, IChunkFile>();
        var restoreTempDir = services.GetRequiredService<TempDirectoryAppSettings>().GetRestoreTempDirectory(root);

        var downloadChunksForBinaryBlock = new DownloadChunksForBinaryBlock(
            loggerFactory: loggerFactory,
            sourceFunc: () => binariesToDownload,
            restoreTempDir: restoreTempDir,
            repo: repo,
            restoredBinaries: restoredBinaries,
            chunksRestored: async (mh, cs) =>
            {
                pointerFilesWaitingForBinaryRestoration.Remove(mh, out var pointerFiles);
                await pointersToRestore.Writer.WriteAsync((cs, pointerFiles.ToArray())); //S21
            },
            chunksHydrating: (mh) =>
            {

            },
            done: () => 
            {
                pointersToRestore.Writer.Complete(); //S29
            });
        var downloadChunksForBinaryTask = downloadChunksForBinaryBlock.GetTask;





        //void removeFromPendingDownload(IChunkFile cf)
        //{
        //    downloadedChunks.AddOrUpdate(
        //        key: cf.Hash, 
        //        addValue: cf, 
        //        updateValueFactory: (_, _) => throw new InvalidOperationException("This should not happen. A chunk should be downloaded only once."));

        //    foreach (var (mh, (allChunkHashes, pendingDownloadChunkHashes)) in chunksForManifest.ToArray())
        //    {
        //        pendingDownloadChunkHashes.RemoveAll(h => h == cf.Hash);

        //        if (!pendingDownloadChunkHashes.Any())
        //        {
        //            //All chunks for this manifest are now downloaded
        //            if (!chunksForManifest.TryRemove(mh, out _))
        //                throw new InvalidOperationException($"Manifest '{mh}' should have been present in the {nameof(chunksForManifest)} list but isn't");

        //            var cfs = allChunkHashes.Select(ch => downloadedChunks[ch]).ToArray();
        //            var pfs = pointerFilesWaitingForManifestRestoration[mh].ToArray();
        //            pointersToRestore.Add((cfs, pfs));
        //        }
        //    }
        //}



        var restorePointerFileBlock = new RestoreBinaryFileBlock(
            loggerFactory: loggerFactory,
            sourceFunc: () => pointersToRestore,
            pointerService: pointerService,
            chunker: services.GetRequiredService<Chunker>(),
            root: root,
            done: () => 
            { 
            });
        var restorePointerFileTask = restorePointerFileBlock.GetTask;

        // TODO DELETE ALL TEMP HYDRATED IN STORAGE ACCOUNT
        // repo.DeleteHydrateFolder();


        await Task.WhenAny(Task.WhenAll(BlockBase.AllTasks), BlockBase.CancellationTask);

        if (BlockBase.AllTasks.Where(t => t.Status == TaskStatus.Faulted) is var ts
            && ts.Any())
        {
            var exceptions = ts.Select(t => t.Exception);
            throw new AggregateException(exceptions);
        }
        //else if (binariesWaitingForManifestCreation.Count > 0 || chunksForManifest.Count > 0)
        //{
        //    //something went wrong
        //    throw new InvalidOperationException("Not all queues are emptied");
        //}

        /*
        //var root = new DirectoryInfo(_options.Path);
        //if (root.Exists && root.EnumerateFiles().Any())
        //{
        //    // TODO use !pf.LocalContentFileInfo.Exists 
        //    _logger.LogWarning("The folder is not empty. There may be lingering files after the restore.");
        //    //TODO LOG WARNING if local root directory contains other things than the pointers with their respecitve localcontentfiles --> will not be overwritten but may be backed up
        //}

        //var dedupedItems = _azureRepository.GetAllManifestHashes()
        //    .SelectMany(manifestHash => _azureRepository
        //        .GetChunkHashesAsync(manifestHash).Result
        //            .Select(chunkHash => new { manifestHash, chunkHash }))
        //    .GroupBy(zz => zz.chunkHash)
        //    .Where(kk => kk.Count() > 1)
        //    .ToList();

        //var orphanedChunksWithoutPointers = _azureRepository.GetAllChunkBlobItems().Select(recbi => recbi.Hash).Except(_azureRepository.GetCurrentEntries(true).Select(z => z.ManifestHash)).ToArray();
        //var orphanedChunksWithoutManifest_Cannotberestored = _azureRepository.GetAllChunkBlobItems().Select(recbi => recbi.Hash).Except(_azureRepository.GetAllManifestHashes()).ToArray();

        //var zz = _azureRepository.GetCurrentEntries(true).ToArray();

        //var ozz = zz.Where(a => orphanedChunksWithoutPointers.Contains(a.ManifestHash));
        */





        return 0;




    }
}