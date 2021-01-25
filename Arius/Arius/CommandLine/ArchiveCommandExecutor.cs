using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;
using Arius.Services;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arius.CommandLine
{
    internal class ArchiveCommandExecutor : ICommandExecutor
    {
        public ArchiveCommandExecutor(ICommandExecutorOptions options,
            ILogger<ArchiveCommandExecutor> logger,
            ILoggerFactory loggerFactory,

            IConfiguration config,
            AzureRepository azureRepository,

            PointerService ps,
            IHashValueProvider h,
            IChunker c,
            IEncrypter e)
        {
            _options = (ArchiveOptions)options;
            _root = new DirectoryInfo(_options.Path);
            _logger = logger;
            _loggerFactory = loggerFactory;

            _config = config;
            _azureRepository = azureRepository;
            _ps = ps;
            _hvp = h;
            _chunker = c;
            _encrypter = e;
        }

        private readonly ArchiveOptions _options;
        private readonly DirectoryInfo _root;
        private readonly ILogger<ArchiveCommandExecutor> _logger;
        private readonly ILoggerFactory _loggerFactory;

        private readonly IConfiguration _config;
        private readonly AzureRepository _azureRepository;
        private readonly PointerService _ps;
        private readonly IHashValueProvider _hvp;
        private readonly IChunker _chunker;
        private readonly IEncrypter _encrypter;


        public int Execute()
        {
            var version = DateTime.Now.ToUniversalTime(); //  !! Table Storage bewaart alles in universal time TODO nadenken over andere impact TODO test dit

            // Define blocks & intermediate variables
            var blocks = new ServiceCollection()
                .AddSingleton<ILoggerFactory>(_loggerFactory)
                .AddLogging()
                    
                .AddSingleton<ArchiveOptions>(_options)
                
                .AddSingleton<IConfiguration>(_config)
                .AddSingleton<AzureRepository>(_azureRepository)
                .AddSingleton<PointerService>(_ps)
                .AddSingleton<IHashValueProvider>(_hvp)
                .AddSingleton<IChunker>(_chunker)
                .AddSingleton<IEncrypter>(_encrypter)


                .AddSingleton<IndexDirectoryBlockProvider>()
                .AddSingleton<AddHashBlockProvider>()
                .AddSingleton<ManifestBlocksProvider>()
                .AddSingleton<ChunkBlockProvider>()
                .AddSingleton<EncryptChunksBlockProvider>()
                .AddSingleton<EnqueueEncryptedChunksForUploadBlockProvider>()
                .AddSingleton<UploadEncryptedChunksBlockProvider>()
                .AddSingleton<EnqueueUploadTaskProvider>()
                .AddSingleton<ReconcileChunksWithManifestsBlockProvider>()
                .AddSingleton<CreateManifestBlockProvider>()
                .AddSingleton<CreatePointerBlockProvider>()
                .AddSingleton<CreatePointerFileEntryIfNotExistsBlockProvider>()
                .AddSingleton<RemoveDeletedPointersTaskProvider>()
                .AddSingleton<ExportToJsonTaskProvider>()
                .AddSingleton<DeleteBinaryFilesTaskProvider>()

                .BuildServiceProvider();

            
            var indexDirectoryBlock = blocks.GetService<IndexDirectoryBlockProvider>()!.GetBlock();


            var addHashBlock = blocks.GetService<AddHashBlockProvider>()!.GetBlock();


            var chunksThatNeedToBeUploadedBeforeManifestCanBeCreated = new Dictionary<BinaryFile, List<HashValue>>(); //Key = BinaryFile, List = HashValue van de Chunks
            var chunkBlock = blocks.GetService<ChunkBlockProvider>()
                !.SetChunksThatNeedToBeUploadedBeforeManifestCanBeCreated(chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
                .GetBlock();


            var manifestBlocksProvider = blocks.GetService<ManifestBlocksProvider>();
            var createIfNotExistManifestBlock = manifestBlocksProvider!.GetCreateIfNotExistsBlock();
            var reconcileManifestBlock = manifestBlocksProvider.GetReconcileBlock();


            var encryptChunksBlock = blocks.GetService<EncryptChunksBlockProvider>()!.GetBlock();


            var uploadQueue = new BlockingCollection<EncryptedChunkFile>();
            var enqueueEncryptedChunksForUploadBlock = blocks.GetService<EnqueueEncryptedChunksForUploadBlockProvider>()
                !.AddUploadQueue(uploadQueue)
                .GetBlock();


            var uploadEncryptedChunksBlock =  blocks.GetService<UploadEncryptedChunksBlockProvider>()!.GetBlock();


            var uploadTask = blocks.GetService<EnqueueUploadTaskProvider>()
                !.AddUploadQueue(uploadQueue)
                .AddUploadEncryptedChunkBlock(uploadEncryptedChunksBlock)
                .AddEnqueueEncryptedChunksForUploadBlock(enqueueEncryptedChunksForUploadBlock)
                .GetTask();


            var reconcileChunksWithManifestsBlock = blocks.GetService<ReconcileChunksWithManifestsBlockProvider>()
                !.AddChunksThatNeedToBeUploadedBeforeManifestCanBeCreated(chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
                .GetBlock();

            
            var createManifestBlock = blocks.GetService<CreateManifestBlockProvider>()!.GetBlock();


            var binaryFilesToDelete = new List<BinaryFile>();
            var createPointersBlock = blocks.GetService<CreatePointerBlockProvider>()
                !.AddBinaryFilesToDelete(binaryFilesToDelete)
                .GetBlock();


            var createPointerFileEntryIfNotExistsBlock = blocks.GetService<CreatePointerFileEntryIfNotExistsBlockProvider>()
                !.AddVersion(version)
                .GetBlock();


            var removeDeletedPointersTask = blocks.GetService<RemoveDeletedPointersTaskProvider>()
                !.AddVersion(version)
                .GetTask();


            var exportToJsonTask = blocks.GetService<ExportToJsonTaskProvider>()!.GetTask();


            var deleteBinaryFilesTask = blocks.GetService<DeleteBinaryFilesTaskProvider>()
                !.AddBinaryFilesToDelete(binaryFilesToDelete)
                .GetTask();


            // Set up linking
            var propagateCompletionOptions = new DataflowLinkOptions() {PropagateCompletion = true};
            var doNotPropagateCompletionOptions = new DataflowLinkOptions() {PropagateCompletion = false};

            // A10
            indexDirectoryBlock.LinkTo(
                addHashBlock,
                propagateCompletionOptions);


            // A20
            addHashBlock.LinkTo(
                createIfNotExistManifestBlock,
                propagateCompletionOptions,
                item => item is BinaryFile,
                item => (BinaryFile)item);

            // A30
            addHashBlock.LinkTo(
                createPointerFileEntryIfNotExistsBlock,
                doNotPropagateCompletionOptions,
                item => item is PointerFile,
                item => (PointerFile)item);

            //addHashBlock.LinkTo(
            //    DataflowBlock.NullTarget<AriusArchiveItem>());

            // A40
            createIfNotExistManifestBlock.LinkTo(
                chunkBlock,
                propagateCompletionOptions,
                x => x.ToProcess,
                x => x.Item);

            // A50
            createIfNotExistManifestBlock.LinkTo(
                reconcileManifestBlock,
                doNotPropagateCompletionOptions,
                x => x.Item);

            // A60
            chunkBlock.LinkTo(
                encryptChunksBlock, 
                propagateCompletionOptions, 
                f => !f.Uploaded,
                f => f.ChunkFile);

            // A70
            chunkBlock.LinkTo(
                reconcileChunksWithManifestsBlock,
                doNotPropagateCompletionOptions,
                f => f.Uploaded,
                cf => cf.ChunkFile.Hash);

            // A80
            encryptChunksBlock.LinkTo(
                enqueueEncryptedChunksForUploadBlock,
                propagateCompletionOptions);


            // A90
            Task.WhenAll(enqueueEncryptedChunksForUploadBlock.Completion)
                .ContinueWith(_ => uploadQueue.CompleteAdding());


            // A100
            Task.WhenAll(uploadTask)
                .ContinueWith(_ => uploadEncryptedChunksBlock.Complete());

            
            // A110
            uploadEncryptedChunksBlock.LinkTo(
                reconcileChunksWithManifestsBlock,
                doNotPropagateCompletionOptions);


            // A115
            Task.WhenAll(uploadEncryptedChunksBlock.Completion, chunkBlock.Completion)
                .ContinueWith(_ => reconcileChunksWithManifestsBlock.Complete());


            // A120
            reconcileChunksWithManifestsBlock.LinkTo(
                createManifestBlock,
                propagateCompletionOptions);


            // A130
            createManifestBlock.LinkTo(
                reconcileManifestBlock,
                doNotPropagateCompletionOptions);


            // A140
            Task.WhenAll(createManifestBlock.Completion, createIfNotExistManifestBlock.Completion)
                .ContinueWith(_ => reconcileManifestBlock.Complete());


            // A150
            reconcileManifestBlock.LinkTo(
                createPointersBlock,
                propagateCompletionOptions);


            // A160
            createPointersBlock.LinkTo(createPointerFileEntryIfNotExistsBlock, 
                doNotPropagateCompletionOptions);


            // A170
            Task.WhenAll(createPointersBlock.Completion, addHashBlock.Completion)
                .ContinueWith(_ => createPointerFileEntryIfNotExistsBlock.Complete());


            // A180
            createPointerFileEntryIfNotExistsBlock.Completion
                .ContinueWith(_ =>
                {
                    removeDeletedPointersTask.Start();
                });

            // A190
            removeDeletedPointersTask
                .ContinueWith(_ => exportToJsonTask.Start());

            // A200
            exportToJsonTask
                .ContinueWith(_ => deleteBinaryFilesTask.Start());


            //Fill the flow
            indexDirectoryBlock.Post(_root);
            indexDirectoryBlock.Complete();


            // Wait for the end
            deleteBinaryFilesTask.Wait();


            return 0;
        }
    }
}