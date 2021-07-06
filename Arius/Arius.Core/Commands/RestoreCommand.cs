using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Core.Commands.Restore;
using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands
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
            this.services = serviceProvider;
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

            
            var pointerFilesToDownload = new BlockingCollection<PointerFile>();

            var indexBlock = new IndexBlock(
                logger: loggerFactory.CreateLogger<IndexBlock>(),
                sourceFunc: () =>
                {
                    return File.GetAttributes(options.Path).HasFlag(FileAttributes.Directory) ?
                        new DirectoryInfo(options.Path) :
                        new FileInfo(options.Path);
                },
                maxDegreeOfParallelism: 1,
                synchronize: options.Synchronize,
                repo: repo,
                pointerService: pointerService,
                pointerToDownload: arg =>
                {
                    var (pf, alreadyRestored) = arg;
                    if (options.Download && !alreadyRestored) //S21
                        pointerFilesToDownload.Add(pf);
                },
                done: () => { });
            var indexTask = indexBlock.GetTask;


            var restoredManifests = new ConcurrentDictionary<ManifestHash, BinaryFile>(); //Key = Manifest

            var processPointerFileBlock = new ProcessPointerFileBlock(
                logger: loggerFactory.CreateLogger<ProcessPointerFileBlock>(),
                sourceFunc: () => pointerFilesToDownload,
                repo: repo,
                pointerService: pointerService,
                alreadyRestored: (_, bf) =>
                {
                    restoredManifests.TryAdd(bf.Hash, bf); //S31 //NOTE: TryAdd returns false if this key is already present but that is OK, we just need a single BinaryFile to be present in order to restore future potential duplicates
                }, 
                done: () => { }
                );
            


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
