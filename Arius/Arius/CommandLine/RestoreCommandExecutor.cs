using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;
using Arius.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arius.CommandLine
{
    internal class RestoreCommandExecutor : ICommandExecutor
    {
        public RestoreCommandExecutor(ICommandExecutorOptions options,
                ILogger<RestoreCommandExecutor> logger,
                ILoggerFactory loggerFactory,

                IConfiguration config,
                AzureRepository azureRepository,

                PointerService ps,
                IHashValueProvider h,
                /*IChunker c, */Chunker chunker, DedupChunker dedupChunker,
                IEncrypter e)
        {
            _options = (RestoreOptions)options;
            _root = new DirectoryInfo(_options.Path);
            _logger = logger;
            _loggerFactory = loggerFactory;

            _config = config;
            _azureRepository = azureRepository;

            _ps = ps;
            _hvp = h;
            //_chunker = c;
            _chunker = chunker;
            _dedupChunker = dedupChunker;
            _encrypter = e;
        }

        private readonly RestoreOptions _options;
        private readonly ILogger<RestoreCommandExecutor> _logger;
        private readonly ILoggerFactory _loggerFactory;

        private readonly IConfiguration _config;
        private readonly AzureRepository _azureRepository;

        private readonly DirectoryInfo _root;
        
        private readonly PointerService _ps;
        private readonly IHashValueProvider _hvp;
        //private readonly IChunker _chunker;
        private readonly Chunker _chunker;
        private readonly DedupChunker _dedupChunker;
        private readonly IEncrypter _encrypter;


        public int Execute()
        {
            if (_root.Exists && _root.EnumerateFiles().Any())
            {
                // use !pf.LocalContentFileInfo.Exists 
                _logger.LogWarning("The folder is not empty. There may be lingering files after the restore.");
                //TODO LOG WARNING if local root directory contains other things than the pointers with their respecitve localcontentfiles --> will not be overwritten but may be backed up
            }


            // Define blocks & intermediate variables
            var blocks = new ServiceCollection()
                .AddSingleton<ILoggerFactory>(_loggerFactory)
                .AddLogging()

                .AddSingleton<RestoreOptions>(_options)

                .AddSingleton<IConfiguration>(_config)
                .AddSingleton<AzureRepository>(_azureRepository)
                .AddSingleton<PointerService>(_ps)
                .AddSingleton<IHashValueProvider>(_hvp)
                .AddSingleton<Chunker>(_chunker)
                .AddSingleton<DedupChunker>(_dedupChunker)
                .AddSingleton<IEncrypter>(_encrypter)


                .AddSingleton<SynchronizeBlockProvider>()
                .AddSingleton<ProcessPointerChunksBlockProvider>()
                .AddSingleton<HydrateBlockProvider>()
                .AddSingleton<DownloadBlockProvider>()
                .AddSingleton<DecryptBlockProvider>()
                .AddSingleton<ReconcilePointersWithChunksBlockProvider>()
                .AddSingleton<MergeBlockProvider>()

                .BuildServiceProvider();


            var synchronizeBlock = blocks.GetService<SynchronizeBlockProvider>()!.GetBlock();


            //var hydrateQueue = new BlockingCollection<RemoteEncryptedChunkBlobItem>();
            //var downloadQueue = new BlockingCollection<RemoteEncryptedChunkBlobItem>();
            //var decryptQueue = new BlockingCollection<EncryptedChunkFile>();

            var hydrateBlockProvider = blocks.GetService<HydrateBlockProvider>();
            var hydrateBlock = hydrateBlockProvider!.GetBlock();

            var downloadBlockProvider = blocks.GetService<DownloadBlockProvider>();
            var enqueueDownloadBlock = downloadBlockProvider!.GetEnqueueBlock();
            var batchingTask = downloadBlockProvider!.GetBatchingTask();
            var downloadBlock = downloadBlockProvider!.GetDownloadBlock();

            var decryptBlock = blocks.GetService<DecryptBlockProvider>()!.GetBlock();


            var reconcilePointersWithChunksBlockProvider = blocks.GetService<ReconcilePointersWithChunksBlockProvider>();
            var reconcilePointerBlock = reconcilePointersWithChunksBlockProvider!.GetReconcilePointerBlock();
            var reconcileChunkBlock = reconcilePointersWithChunksBlockProvider!.GetReconcileChunkBlock();


            var processPointerChunksBlock = blocks.GetService<ProcessPointerChunksBlockProvider>()
                !.SetReconcileChunkBlock(reconcileChunkBlock)
                .SetHydrateBlock(hydrateBlock)
                .SetEnqueueDownloadBlock(enqueueDownloadBlock)
                .SetDecryptBlock(decryptBlock)
                .GetBlock();




            var mergeBlock = blocks.GetService<MergeBlockProvider>()!.GetBlock();


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
                r => true, // r.State == PointerState.NotYetMerged, //don't care what the state is we propagate to reconciliatoin
                r => r.PointerFile);

            processPointerChunksBlock.Completion.ContinueWith(_ => 
            {
                enqueueDownloadBlock.Complete(); //R81
                hydrateBlock.Complete(); //R91
            });

            // R71
            Task.WhenAll(processPointerChunksBlock.Completion, downloadBlock.Completion)
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
                synchronizeBlock.Post(_root);
                synchronizeBlock.Complete();
            }
            else if (_options.Download)
            {
                // R20
                throw new NotFiniteNumberException();
            }


            // Wait for the end
            // R110
            Task.WaitAll(
                synchronizeBlock.Completion,
                processPointerChunksBlock.Completion,
                mergeBlock.Completion,
                hydrateBlock.Completion);

            _config.DownloadTempDir(_root).DeleteEmptySubdirectories(true);

            if (hydrateBlockProvider.AtLeastOneHydrated)
            {
                // Show a warning
            }
            else
            {
                // Delete all
            }

            return 0;



            //if (_options.Synchronize)
            //    Synchronize();

            //    if (_options.Download)
            //        Download();
            //}
            ////else if (File.Exists(path) && path.EndsWith(".arius"))
            ////{
            ////    // Restore one file

            ////}
            //else
            //{
            //    throw new NotImplementedException();
            //}

            //return 0;
        }
    }

    
}
