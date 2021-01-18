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
            ////TODO Simulate
            ////TODO MINSIZE
            ////TODO CHeck if the archive is deduped and password by checking the first amnifest file


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
                .AddSingleton<AddRemoteManifestBlockProvider>()
                .AddSingleton<GetChunksForUploadBlockProvider>()
                .AddSingleton<EncryptChunksBlockProvider>()
                .AddSingleton<EnqueueEncryptedChunksForUploadBlockProvider>()
                .AddSingleton<UploadEncryptedChunksBlockProvider>()
                .AddSingleton<UploadTaskProvider>()
                .AddSingleton<ReconcileChunksWithManifestsBlockProvider>()
                .AddSingleton<CreateManifestBlockProvider>()
                .AddSingleton<ReconcileBinaryFilesWithManifestBlockProvider>()
                .AddSingleton<CreatePointerBlockProvider>()
                .AddSingleton<CreatePointerFileEntryIfNotExistsBlockProvider>()
                .AddSingleton<RemoveDeletedPointersTaskProvider>()
                .AddSingleton<ExportToJsonTaskProvider>()
                .AddSingleton<DeleteBinaryFilesTaskProvider>()

                .BuildServiceProvider();

            
            var indexDirectoryBlock = blocks.GetService<IndexDirectoryBlockProvider>()!.GetBlock();


            var addHashBlock = blocks.GetService<AddHashBlockProvider>()!.GetBlock();


            var uploadedManifestHashes = new List<HashValue>(_azureRepository.GetAllManifestHashes());
            var addRemoteManifestBlock = blocks.GetService<AddRemoteManifestBlockProvider>()
                !.AddUploadedManifestHashes(uploadedManifestHashes)
                .GetBlock();


            var chunksThatNeedToBeUploadedBeforeManifestCanBeCreated = new Dictionary<BinaryFile, List<HashValue>>(); //Key = BinaryFile, List = HashValue van de Chunks
            var getChunksForUploadBlock = blocks.GetService<GetChunksForUploadBlockProvider>()
                !.SetChunksThatNeedToBeUploadedBeforeManifestCanBeCreated(chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
                .GetBlock();

            
            var encryptChunksBlock = blocks.GetService<EncryptChunksBlockProvider>()!.GetBlock();


            var uploadQueue = new BlockingCollection<EncryptedChunkFile>();
            var enqueueEncryptedChunksForUploadBlock = blocks.GetService<EnqueueEncryptedChunksForUploadBlockProvider>()
                !.AddUploadQueue(uploadQueue)
                .GetBlock();


            var uploadEncryptedChunksBlock =  blocks.GetService<UploadEncryptedChunksBlockProvider>()!.GetBlock();


            var uploadTask = blocks.GetService<UploadTaskProvider>()
                !.AddUploadQueue(uploadQueue)
                .AddUploadEncryptedChunkBlock(uploadEncryptedChunksBlock)
                .AddEnqueueEncryptedChunksForUploadBlock(enqueueEncryptedChunksForUploadBlock)
                .GetTask();


            var reconcileChunksWithManifestsBlock = blocks.GetService<ReconcileChunksWithManifestsBlockProvider>()
                !.AddChunksThatNeedToBeUploadedBeforeManifestCanBeCreated(chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
                .GetBlock();

            
            var createManifestBlock = blocks.GetService<CreateManifestBlockProvider>()!.GetBlock();


            var reconcileBinaryFilesWithManifestBlock = blocks.GetService<ReconcileBinaryFilesWithManifestBlockProvider>()
                !.AddUploadedManifestHashes(uploadedManifestHashes)
                .GetBlock();


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

            // 10
            indexDirectoryBlock.LinkTo(
                addHashBlock,
                propagateCompletionOptions);


            // 20
            addHashBlock.LinkTo(
                addRemoteManifestBlock,
                propagateCompletionOptions,
                x => x is BinaryFile);

            // 30
            addHashBlock.LinkTo(
                createPointerFileEntryIfNotExistsBlock,
                doNotPropagateCompletionOptions,
                x => x is PointerFile,
                f => (PointerFile)f);

            //addHashBlock.LinkTo(
            //    DataflowBlock.NullTarget<AriusArchiveItem>());


            // 40
            addRemoteManifestBlock.LinkTo(
                getChunksForUploadBlock,
                propagateCompletionOptions, 
                binaryFile => !binaryFile.ManifestHash.HasValue);

            // 50
            addRemoteManifestBlock.LinkTo(
                reconcileBinaryFilesWithManifestBlock,
                doNotPropagateCompletionOptions,
                binaryFile => binaryFile.ManifestHash.HasValue);


            // 60
            getChunksForUploadBlock.LinkTo(
                encryptChunksBlock, 
                propagateCompletionOptions, 
                f => !f.Uploaded);

            // 70
            getChunksForUploadBlock.LinkTo(
                reconcileChunksWithManifestsBlock,
                doNotPropagateCompletionOptions,
                f => f.Uploaded,
                cf => cf.Hash);


            // 80
            encryptChunksBlock.LinkTo(
                enqueueEncryptedChunksForUploadBlock,
                propagateCompletionOptions);


            // 90
            Task.WhenAll(enqueueEncryptedChunksForUploadBlock.Completion)
                .ContinueWith(_ => uploadQueue.CompleteAdding());


            // 100
            Task.WhenAll(uploadTask)
                .ContinueWith(_ => uploadEncryptedChunksBlock.Complete());

            
            // 110
            uploadEncryptedChunksBlock.LinkTo(
                reconcileChunksWithManifestsBlock,
                doNotPropagateCompletionOptions);


            // 115
            Task.WhenAll(uploadEncryptedChunksBlock.Completion, getChunksForUploadBlock.Completion)
                .ContinueWith(_ => reconcileChunksWithManifestsBlock.Complete());


            // 120
            reconcileChunksWithManifestsBlock.LinkTo(
                createManifestBlock,
                propagateCompletionOptions);

            
            // 130
            createManifestBlock.LinkTo(
                reconcileBinaryFilesWithManifestBlock,
                doNotPropagateCompletionOptions);


            // 140
            Task.WhenAll(createManifestBlock.Completion, addRemoteManifestBlock.Completion)
                .ContinueWith(_ => reconcileBinaryFilesWithManifestBlock.Complete());


            // 150
            reconcileBinaryFilesWithManifestBlock.LinkTo(
                createPointersBlock,
                propagateCompletionOptions);


            // 160
            createPointersBlock.LinkTo(createPointerFileEntryIfNotExistsBlock, 
                doNotPropagateCompletionOptions);


            // 170
            Task.WhenAll(createPointersBlock.Completion, addHashBlock.Completion)
                .ContinueWith(_ => createPointerFileEntryIfNotExistsBlock.Complete());


            // 180
            createPointerFileEntryIfNotExistsBlock.Completion
                .ContinueWith(_ =>
                {
                    removeDeletedPointersTask.Start();
                });

            // 190
            removeDeletedPointersTask
                .ContinueWith(_ => exportToJsonTask.Start());

            // 200
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