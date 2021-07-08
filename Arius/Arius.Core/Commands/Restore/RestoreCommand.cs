using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands.Restore
{
    internal class RestoreCommand : ICommand //This class is internal but the interface is public for use in the Facade
    {
        internal interface IOptions
        {
            string Path { get; }
            bool Download { get; }
            bool Synchronize { get; }
        }

        public RestoreCommand(IOptions options,
            ILogger<RestoreCommand> logger,
            IServiceProvider serviceProvider)
        {
            this.options = options;
            this.logger = logger;
            services = serviceProvider;
        }

        private readonly IOptions options;
        private readonly ILogger<RestoreCommand> logger;
        private readonly IServiceProvider services;

        IServiceProvider ICommand.Services => services;

        public async Task<int> Execute()
        {
            var loggerFactory = services.GetRequiredService<ILoggerFactory>();
            var repo = services.GetRequiredService<Repository>();
            var pointerService = services.GetRequiredService<PointerService>();

            FileSystemInfo pathToRestore;
            DirectoryInfo restoreTempDir;
            
            if (File.GetAttributes(options.Path).HasFlag(FileAttributes.Directory))
            {
                var root = new DirectoryInfo(options.Path);
                pathToRestore = root;
                restoreTempDir = services.GetRequiredService<TempDirectoryAppSettings>().GetRestoreTempDirectory(root);
            }
            else
            {
                var file = new FileInfo(options.Path);
                pathToRestore = file;
                restoreTempDir = services.GetRequiredService<TempDirectoryAppSettings>().GetRestoreTempDirectory(file.Directory);
            }


            var pointerFilesToDownload = new BlockingCollection<PointerFile>();
            var restoredManifests = new ConcurrentDictionary<ManifestHash, BinaryFile>();

            logger.LogInformation("Determining PointerFiles to restore...");

            var indexBlock = new IndexBlock(
                logger: loggerFactory.CreateLogger<IndexBlock>(),
                sourceFunc: () => pathToRestore, //S10
                maxDegreeOfParallelism: 2,
                synchronize: options.Synchronize,
                repo: repo,
                pointerService: pointerService,
                indexedPointerFile: arg =>
                {
                    var (pf, bf) = arg;
                    if (options.Download && bf is null)
                        pointerFilesToDownload.Add(pf); //S11
                    if (bf is not null)
                        restoredManifests.TryAdd(bf.Hash, bf); //S12 //NOTE: TryAdd returns false if this key is already present but that is OK, we just need a single BinaryFile to be present in order to restore future potential duplicates
                },
                done: () => 
                {
                    pointerFilesToDownload.CompleteAdding(); //S13
                });
            var indexTask = indexBlock.GetTask;

            await indexTask;

            logger.LogInformation($"Determining PointerFiles to restore... done. {pointerFilesToDownload.Count} PointerFiles to restore.");

            var chunksForManifest = new ConcurrentDictionary<ManifestHash, (ChunkHash[] All, List<ChunkHash> PendingDownload)>();
            var pointersToRestore = new BlockingCollection<(PointerFile PointerFile, BinaryFile Binary)>();

            var processPointerFileBlock = new ProcessPointerFileBlock(
                logger: loggerFactory.CreateLogger<ProcessPointerFileBlock>(),
                sourceFunc: () => pointerFilesToDownload,
                restoreTempDir: restoreTempDir,
                repo: repo,
                restoredManifests: restoredManifests,
                manifestRestored: (pf, bf) => pointersToRestore.Add((pf, bf)), //S21
                chunksForManifest: (manifestHash, chunkHashes) =>
                {
                    chunksForManifest.AddOrUpdate( //S22
                        key: manifestHash,
                        addValue: (All: chunkHashes, PendingDownload: chunkHashes.ToList()), //Add the full list of chunks (for writing the manifest later) and a modifyable list of chunks (for reconciliation upon download for triggering manifest creation)
                        updateValueFactory: (_, _) => throw new InvalidOperationException("This should not happen. Once a BinaryFile is emitted for chunking, the chunks should not be updated"));
                },
                done: () => { }
                );
            var processPointerFileTask = processPointerFileBlock.GetTask;



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
}
