using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Services;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands
{
    public class ArchiveCommandOptions : Facade.Facade.IOptions,
            ArchiveCommand.IOptions,

            UploadEncryptedChunksBlockProvider.IOptions,
            RemoveDeletedPointersTaskProvider.IOptions,
            DeleteBinaryFilesTaskProvider.IOptions,

            AzureRepository.IOptions,
            IBlobCopier.IOptions,
            IChunker.IOptions,
            IEncrypter.IOptions,
            IHashValueProvider.IOptions
    {
        public string AccountName { get; init; }
        public string AccountKey { get; init; }
        public string Passphrase { get; init; }
        public bool FastHash { get; init; }
        public string Container { get; init; }
        public bool RemoveLocal { get; init; }
        public AccessTier Tier { get; init; }
        public bool Dedup { get; init; }
        public string Path { get; init; }
    }

    internal class ArchiveCommand : ICommand //This class is internal but the interface is public for use in the Facade
    {
        internal interface IOptions
        {
            string Path { get; }
        }

        public ArchiveCommand(IOptions options,
            ILogger<ArchiveCommand> logger,
            IServiceProvider serviceProvider)
        {
            this.logger = logger;
            services = serviceProvider;

            root = new DirectoryInfo(options.Path);
        }

        internal static void ConfigureServices(IServiceCollection coll/*, Facade.Facade.ArchiveCommandOptions options*/)
        {
            coll
                .AddSingleton<IndexDirectoryBlockProvider>()
                .AddSingleton<AddHashBlockProvider>()
                .AddSingleton<ManifestBlocksProvider>()
                .AddSingleton<ChunkBlockProvider>()
                .AddSingleton<EncryptChunksBlockProvider>()
                .AddSingleton<EnqueueEncryptedChunksForUploadBlockProvider>()
                .AddSingleton<CreateUploadBatchesTaskProvider>()
                .AddSingleton<UploadEncryptedChunksBlockProvider>()
                .AddSingleton<ReconcileChunksWithManifestsBlockProvider>()
                .AddSingleton<CreateManifestBlockProvider>()
                .AddSingleton<CreatePointerBlockProvider>()
                .AddSingleton<CreatePointerFileEntryIfNotExistsBlockProvider>()
                .AddSingleton<ValidateBlockProvider>()
                .AddSingleton<RemoveDeletedPointersTaskProvider>()
                .AddSingleton<ExportToJsonTaskProvider>()
                .AddSingleton<DeleteBinaryFilesTaskProvider>();

            //// Create the options object
            //var options = new Options
            //{
            //    AccountName = accountName,
            //    AccountKey = accountKey,
            //    Passphrase = passphrase,
            //    FastHash = fastHash,
            //    Container = container,
            //    RemoveLocal = removeLocal,
            //    Tier = tier,
            //    //Dedup = dedup,
            //    Path = path
            //};

            //// Add the options for the Blocks
            //coll
            //    .AddSingleton<ArchiveCommandExecutor.IOptions>(options)
            //    //.AddSingleton<IndexDirectoryBlockProvider>()
            //    //.AddSingleton<AddHashBlockProvider>()
            //    //.AddSingleton<ManifestBlocksProvider>()
            //    //.AddSingleton<ChunkBlockProvider>()
            //    //.AddSingleton<EncryptChunksBlockProvider>()
            //    //.AddSingleton<EnqueueEncryptedChunksForUploadBlockProvider>()
            //    //.AddSingleton<CreateUploadBatchesTaskProvider>()
            //    .AddSingleton<UploadEncryptedChunksBlockProvider.IOptions>(options)
            //    //.AddSingleton<ReconcileChunksWithManifestsBlockProvider>()
            //    //.AddSingleton<CreateManifestBlockProvider>()
            //    //.AddSingleton<CreatePointerBlockProvider>()
            //    //.AddSingleton<CreatePointerFileEntryIfNotExistsBlockProvider>()
            //    //.AddSingleton<ValidateBlockProvider>()
            //    .AddSingleton<RemoveDeletedPointersTaskProvider.IOptions>(options)
            //    //.AddSingleton<ExportToJsonTaskProvider>()
            //    .AddSingleton<DeleteBinaryFilesTaskProvider.IOptions>(options);
        }

        private readonly ILogger<ArchiveCommand> logger;
        private readonly IServiceProvider services;
        private readonly DirectoryInfo root;

        IServiceProvider ICommand.Services => services;

        public async Task<int> Execute()
        {
            var version = DateTime.Now.ToUniversalTime(); //  !! Table Storage bewaart alles in universal time TODO nadenken over andere impact TODO test dit

            // Define blocks & intermediate variables
            var indexDirectoryBlock = services.GetRequiredService<IndexDirectoryBlockProvider>().GetBlock();


            var addHashBlock = services.GetRequiredService<AddHashBlockProvider>().GetBlock();


            var chunksThatNeedToBeUploadedBeforeManifestCanBeCreated = new Dictionary<BinaryFile, List<HashValue>>(); //Key = BinaryFile, List = HashValue van de Chunks
            var chunkBlock = services.GetRequiredService<ChunkBlockProvider>()
                .SetChunksThatNeedToBeUploadedBeforeManifestCanBeCreated(chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
                .GetBlock();


            var manifestBlocksProvider = services.GetRequiredService<ManifestBlocksProvider>();
            var createIfNotExistManifestBlock = manifestBlocksProvider.GetCreateIfNotExistsBlock();
            var reconcileManifestBlock = manifestBlocksProvider.GetReconcileBlock();


            var encryptChunksBlock = services.GetRequiredService<EncryptChunksBlockProvider>().GetBlock();


            var uploadQueue = new BlockingCollection<EncryptedChunkFile>();
            var enqueueEncryptedChunksForUploadBlock = services.GetRequiredService<EnqueueEncryptedChunksForUploadBlockProvider>()
                .AddUploadQueue(uploadQueue)
                .GetBlock();


            var uploadEncryptedChunksBlock = services.GetRequiredService<UploadEncryptedChunksBlockProvider>().GetBlock();


            var createUploadBatchesTask = services.GetRequiredService<CreateUploadBatchesTaskProvider>()
                .AddUploadQueue(uploadQueue)
                .AddUploadEncryptedChunkBlock(uploadEncryptedChunksBlock)
                .AddEnqueueEncryptedChunksForUploadBlock(enqueueEncryptedChunksForUploadBlock)
                .GetTask();


            var reconcileChunksWithManifestsBlock = services.GetRequiredService<ReconcileChunksWithManifestsBlockProvider>()
                .AddChunksThatNeedToBeUploadedBeforeManifestCanBeCreated(chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
                .GetBlock();


            var createManifestBlock = services.GetRequiredService<CreateManifestBlockProvider>().GetBlock();


            var binaryFilesToDelete = new List<BinaryFile>();
            var createPointersBlock = services.GetRequiredService<CreatePointerBlockProvider>()
                .AddBinaryFilesToDelete(binaryFilesToDelete)
                .GetBlock();


            var createPointerFileEntryIfNotExistsBlock = services.GetRequiredService<CreatePointerFileEntryIfNotExistsBlockProvider>()
                .AddVersion(version)
                .GetBlock();


            var validateBlock = services.GetRequiredService<ValidateBlockProvider>()
                .GetBlock();

            var removeDeletedPointersTask = services.GetRequiredService<RemoveDeletedPointersTaskProvider>()
                .AddVersion(version)
                .GetTask();


            var exportToJsonTask = services.GetRequiredService<ExportToJsonTaskProvider>().GetTask();


            var deleteBinaryFilesTask = services.GetRequiredService<DeleteBinaryFilesTaskProvider>()
                .AddBinaryFilesToDelete(binaryFilesToDelete)
                .GetTask();


            // Set up linking
            var propagateCompletionOptions = new DataflowLinkOptions() { PropagateCompletion = true };
            var doNotPropagateCompletionOptions = new DataflowLinkOptions() { PropagateCompletion = false };

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
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed // Used in chaining the TPL flows, expect execution when the .Completion fires
            Task.WhenAll(enqueueEncryptedChunksForUploadBlock.Completion)
                .ContinueWith(_ =>
                {
                    logger.LogDebug("Passing A90");
                    uploadQueue.CompleteAdding(); //Mark the Queue as completed adding, also if enqueueEncryptedChunksForUploadBlock is faulted

                    //TODO KARL error handling if an error occurs here
                });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            // A100
            Task.WhenAll(createUploadBatchesTask)
                .ContinueWith(_ =>
                {
                    logger.LogDebug("Passing A100");
                    uploadEncryptedChunksBlock.Complete();

                    //TODO KARL error handling
                });

            // A110
            uploadEncryptedChunksBlock.LinkTo(
                reconcileChunksWithManifestsBlock,
                doNotPropagateCompletionOptions);


            // A115
            reconcileChunksWithManifestsBlock.JoinCompletion(
                () => logger.LogDebug("Passing A115 - Completion"),
                () => logger.LogDebug("Passing A115 - Faulted"),
                uploadEncryptedChunksBlock, chunkBlock);

            //Task.WhenAll(uploadEncryptedChunksBlock.Completion, chunkBlock.Completion)
            //    .ContinueWith(_ =>
            //    {
            //        _logger.LogDebug("Passing A115");
            //        reconcileChunksWithManifestsBlock.Complete();
            //    });


            // A120
            reconcileChunksWithManifestsBlock.LinkTo(
                createManifestBlock,
                propagateCompletionOptions);


            // A130
            createManifestBlock.LinkTo(
                reconcileManifestBlock,
                doNotPropagateCompletionOptions);


            // A140
            reconcileManifestBlock.JoinCompletion(
                () => logger.LogDebug("Passing A140 - Completion"),
                () => logger.LogDebug("Passing A140 - Faulted"),
                createManifestBlock, createIfNotExistManifestBlock);


            // A150
            reconcileManifestBlock.LinkTo(
                createPointersBlock,
                propagateCompletionOptions);


            // A160
            createPointersBlock.LinkTo(
                createPointerFileEntryIfNotExistsBlock,
                doNotPropagateCompletionOptions);


            // A170
            createPointerFileEntryIfNotExistsBlock.JoinCompletion(
                () => logger.LogDebug("Passing A170 - Completion"),
                () => logger.LogDebug("Passing A170 - Faulted"),
                createPointersBlock, addHashBlock);

            // A175
            createPointerFileEntryIfNotExistsBlock.LinkTo(
                validateBlock, propagateCompletionOptions);


            //Fill the flow
            indexDirectoryBlock.Post(root);
            indexDirectoryBlock.Complete();

            //Await for its completion
            await validateBlock.Completion;

            // A180
            logger.LogDebug("Passing A180");
            await Task.Run(removeDeletedPointersTask);

            // A190
            logger.LogDebug("Passing A190");
            await Task.Run(exportToJsonTask);

            // A200
            logger.LogDebug("Passing A200");
            await Task.Run(deleteBinaryFilesTask);

            logger.LogInformation("Done");

            return 0;
        }
    }
}