using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Models;
using Arius.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands
{
    internal class RestoreCommandExecutor : ICommandExecutor
    {
        internal interface IOptions
        {
            string Path { get; }
            bool Download { get; }
            bool Synchronize { get; }
        }

        public RestoreCommandExecutor(IOptions options,
            ILogger<RestoreCommandExecutor> logger,
            IServiceProvider serviceProvider)
        {
            _options = options;
            _logger = logger;
            blocks = serviceProvider;
        }

        public static void AddProviders(IServiceCollection coll)
        {
            coll
                .AddSingleton<SynchronizeBlockProvider>()
                .AddSingleton<ProcessPointerChunksBlockProvider>()
                .AddSingleton<HydrateBlockProvider>()
                .AddSingleton<DownloadBlockProvider>()
                .AddSingleton<DecryptBlockProvider>()
                .AddSingleton<ReconcilePointersWithChunksBlockProvider>()
                .AddSingleton<MergeBlockProvider>();
        }

        private readonly IOptions _options;
        private readonly ILogger<RestoreCommandExecutor> _logger;
        private readonly IServiceProvider blocks;

        public async Task<int> Execute()
        {
            var root = new DirectoryInfo(_options.Path);
            if (root.Exists && root.EnumerateFiles().Any())
            {
                // use !pf.LocalContentFileInfo.Exists 
                _logger.LogWarning("The folder is not empty. There may be lingering files after the restore.");
                //TODO LOG WARNING if local root directory contains other things than the pointers with their respecitve localcontentfiles --> will not be overwritten but may be backed up
            }


            // Define blocks & intermediate variables


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




            var synchronizeBlock = blocks.GetRequiredService<SynchronizeBlockProvider>().GetBlock();

            var hydrateBlockProvider = blocks.GetRequiredService<HydrateBlockProvider>();
            var hydrateBlock = hydrateBlockProvider.GetBlock();

            var downloadBlockProvider = blocks.GetRequiredService<DownloadBlockProvider>();
            var enqueueDownloadBlock = downloadBlockProvider.GetEnqueueBlock();
            var batchingTask = downloadBlockProvider.GetBatchingTask();
            var downloadBlock = downloadBlockProvider.GetDownloadBlock();

            var decryptBlock = blocks.GetRequiredService<DecryptBlockProvider>().GetBlock();


            var reconcilePointersWithChunksBlockProvider = blocks.GetRequiredService<ReconcilePointersWithChunksBlockProvider>();
            var reconcilePointerBlock = reconcilePointersWithChunksBlockProvider.GetReconcilePointerBlock();
            var reconcileChunkBlock = reconcilePointersWithChunksBlockProvider.GetReconcileChunkBlock();


            var processPointerChunksBlock = blocks.GetRequiredService<ProcessPointerChunksBlockProvider>()
                .SetReconcileChunkBlock(reconcileChunkBlock)
                .SetHydrateBlock(hydrateBlock)
                .SetEnqueueDownloadBlock(enqueueDownloadBlock)
                .SetDecryptBlock(decryptBlock)
                .GetBlock();




            var mergeBlock = blocks.GetRequiredService<MergeBlockProvider>().GetBlock();


            // Set up linking
            var propagateCompletionOptions = new DataflowLinkOptions() { PropagateCompletion = true };
            var doNotPropagateCompletionOptions = new DataflowLinkOptions() { PropagateCompletion = false };


            // R30
            synchronizeBlock.LinkTo(
                DataflowBlock.NullTarget<PointerFile>(),
                _ => !_options.Download);

            // R40
            synchronizeBlock.LinkTo(
                processPointerChunksBlock,
                propagateCompletionOptions,
                _ => _options.Download);

            // R50
            processPointerChunksBlock.LinkTo(
                DataflowBlock.NullTarget<PointerFile>(),
                propagateCompletionOptions,
                r => r.State == ProcessPointerChunksBlockProvider.PointerState.Restored,
                r => r.PointerFile); //TODO delete pointer if !keepPointer

            // R602
            processPointerChunksBlock.LinkTo(
                reconcilePointerBlock,
                propagateCompletionOptions,
                //r => true, // r.State == PointerState.NotYetMerged, //don't care what the state is we propagate to reconciliatoin
                r => r.PointerFile);

            processPointerChunksBlock.Completion.ContinueWith(_ =>
            {
                enqueueDownloadBlock.Complete(); //R81
                hydrateBlock.Complete(); //R91
            });

            // R71
            Task.WhenAll(reconcilePointerBlock.Completion, /*processPointerChunksBlock.Completion, */downloadBlock.Completion)
                .ContinueWith(_ => decryptBlock.Complete());


            // R82
            downloadBlock.LinkTo(
                decryptBlock,
                doNotPropagateCompletionOptions);

            // R72
            decryptBlock.LinkTo(
                reconcileChunkBlock,
                doNotPropagateCompletionOptions);

            // R61
            Task.WhenAll(processPointerChunksBlock.Completion, decryptBlock.Completion)
                .ContinueWith(_ => reconcileChunkBlock.Complete());

            //R62
            reconcileChunkBlock.LinkTo(
                mergeBlock,
                propagateCompletionOptions);


            //Fill the flow
            if (_options.Synchronize)
            {
                // R10
                synchronizeBlock.Post(root);
                synchronizeBlock.Complete();
            }
            else if (_options.Download)
            {
                // R20
                throw new NotImplementedException();
            }


            // Wait for the end
            // R110
            Task.WaitAll(
                synchronizeBlock.Completion,
                processPointerChunksBlock.Completion,
                mergeBlock.Completion,
                hydrateBlock.Completion);

            blocks.GetRequiredService<TempDirectoryAppSettings>().RestoreTempDirectory(root).DeleteEmptySubdirectories(true);

            if (hydrateBlockProvider.AtLeastOneHydrating)
            {
                // Show a warning
                _logger.LogWarning("WARNING: Not all files are restored as chunks are still being hydrated. Please run the restore operation again in 12-24 hours.");
            }
            else
            {
                // Delete all local
                //TODO

                //Delete hydration directory
                blocks.GetRequiredService<AzureRepository>().DeleteHydrateFolder();
            }

            return 0;
        }
    }
}
