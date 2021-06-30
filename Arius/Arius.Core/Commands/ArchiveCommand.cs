using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Arius.Core.Commands
{
    internal class ArchiveCommand : ICommand
    {
        internal interface IOptions
        {
            bool RemoveLocal { get; }
            AccessTier Tier { get; }
            string Path { get; }
        }

        public ArchiveCommand(IOptions options,
            ILogger<ArchiveCommand> logger,
            IServiceProvider serviceProvider)
        {
            this.options = options;
            this.logger = logger;
            this.services = serviceProvider;
        }

        private readonly IOptions options;
        private readonly ILogger<ArchiveCommand> logger;
        private readonly IServiceProvider services;

        internal static void AddBlockProviders(IServiceCollection coll/*, Facade.Facade.ArchiveCommandOptions options*/)
        {
        }

        IServiceProvider ICommand.Services => services;


        public async Task<int> Execute()
        {
            var loggerFactory = services.GetRequiredService<ILoggerFactory>();
            var repo = services.GetRequiredService<Repository>();
            var versionUtc = DateTime.Now.ToUniversalTime(); //  !! Table Storage bewaart alles in universal time TODO nadenken over andere impact TODO test dit
            var pointerFileService = services.GetRequiredService<PointerService>();

            var filesToHash = new BlockingCollection<IFile>();

            var indexBlock = new IndexBlock(
                logger: loggerFactory.CreateLogger<IndexBlock>(),
                root: new DirectoryInfo(options.Path),
                indexedFile: (file) => filesToHash.Add(file),
                done: () => filesToHash.CompleteAdding()); //B210
            var indexTask = indexBlock.GetTask;


            var pointerFileEntriesToCreate = new BlockingCollection<PointerFile>();
            var binariesToUpload = new BlockingCollection<BinaryFile>();

            var hashBlock = new HashBlock(
                logger: loggerFactory.CreateLogger<HashBlock>(),
                //continueWhile: () => !indexedFiles.IsCompleted,
                source: filesToHash,
                maxDegreeOfParallelism: 1 /*2*/ /*Environment.ProcessorCount */,
                hashedPointerFile: (pf) => pointerFileEntriesToCreate.Add(pf), //B301
                hashedBinaryFile: (bf) => binariesToUpload.Add(bf), //B302
                hvp: services.GetRequiredService<IHashValueProvider>(),
                done: () => binariesToUpload.CompleteAdding()); //B310
            var hashTask = hashBlock.GetTask;


            var binariesToDelete = new BlockingCollection<BinaryFile>();
            var binariesToChunk = new BlockingCollection<BinaryFile>();
            var binariesWaitingForManifestCreation = new ConcurrentDictionary<HashValue, ConcurrentBag<BinaryFile>>(); //Key: ManifestHash. Values (BinaryFiles) that are waiting for the Keys (Manifests) to be created
            var pointersToCreate = new BlockingCollection<BinaryFile>();

            var processHashedBinaryBlock = new ProcessHashedBinaryBlock(
                logger: loggerFactory.CreateLogger<ProcessHashedBinaryBlock>(),
                //continueWhile: () => !createManifest.IsCompleted,
                source: binariesToUpload,
                repo: repo,
                binaryFileAlreadyBackedUp: (bf) => binariesToDelete.Add(bf), //B401
                uploadBinaryFile: (bf) => binariesToChunk.Add(bf),  //B402
                manifestExists: (bf) => pointersToCreate.Add(bf), //B403
                waitForCreatedManifest: (bf) => //B404
                {
                    binariesWaitingForManifestCreation.AddOrUpdate(
                        key: bf.Hash,
                        addValue: new() { bf },
                        updateValueFactory: (h, bag) =>
                        {
                            bag.Add(bf);
                            return bag;
                        });
                },
                done: () => binariesToChunk.CompleteAdding()); //B410
            var processHashedBinaryTask = processHashedBinaryBlock.GetTask;


            var chunksToProcess = new BlockingCollection<IChunkFile>();
            var chunksForManifest = new ConcurrentDictionary<HashValue, (HashValue[] All, List<HashValue> PendingUpload)>(); //Key: ManifestHash, Value: ChunkHashes. 

            var chunkBlock = new ChunkBlock(
                logger: loggerFactory.CreateLogger<ChunkBlock>(),
                source: binariesToChunk,
                maxDegreeOfParallelism: 1 /*2*/,
                chunker: services.GetRequiredService<Chunker>(),
                chunkedBinary: (binaryFile, chunkFiles) => 
                {
                    //B501
                    chunksToProcess.AddFromEnumerable(chunkFiles, false);

                    //B502
                    var chs = chunkFiles.Select(ch => ch.Hash).ToArray();
                    chunksForManifest.AddOrUpdate( 
                        key: binaryFile.Hash,
                        addValue: (All: chs, PendingUpload: chs.ToList()), //Add the full list of chunks (for writing the manifest later) and a modifyable list of chunks (for reconciliation upon upload for triggering manifest creation)
                        updateValueFactory: (_, _) => throw new InvalidOperationException("This should not happen. Once a BinaryFile is emitted for chunking, the chunks should not be updated"));
                },
                done: () => chunksToProcess.CompleteAdding()); //B510
            var chunkTask = chunkBlock.GetTask;


            var chunksToEncrypt = new BlockingCollection<IChunkFile>();
            var manifestsToCreate = new BlockingCollection<(HashValue ManifestHash, HashValue[] ChunkHashes)>();

            var processChunkBlock = new ProcessChunkBlock(
                logger: loggerFactory.CreateLogger<ProcessChunkBlock>(),
                source: chunksToProcess,
                repo: repo,
                chunkToUpload: (cf) => chunksToEncrypt.Add(cf), //B601
                chunkAlreadyUploaded: (h) => removeFromPendingUpload(h), //B602
                done: () => chunksToEncrypt.CompleteAdding()); //B610
            var processChunkTask = processChunkBlock.GetTask;


            var chunksToBatchForUpload = new BlockingCollection<EncryptedChunkFile>();

            var encryptChunkBlock = new EncryptChunkBlock(
                logger: loggerFactory.CreateLogger<EncryptChunkBlock>(),
                source: chunksToEncrypt,
                maxDegreeOfParallelism: 1 /*2*/,
                tempDirAppSettings: services.GetRequiredService<TempDirectoryAppSettings>(),
                encrypter: services.GetRequiredService<IEncrypter>(),
                chunkEncrypted: (ecf) => chunksToBatchForUpload.Add(ecf), //B701
                done: () => chunksToBatchForUpload.CompleteAdding()); //B710
            var encyptChunkBlockTask = encryptChunkBlock.GetTask;


            var batchesToUpload = new BlockingCollection<EncryptedChunkFile[]>();

            var createUploadBatchBlock = new CreateUploadBatchBlock(
                logger: loggerFactory.CreateLogger<CreateUploadBatchBlock>(),
                source: chunksToBatchForUpload,
                azCopyAppSettings: services.GetRequiredService<AzCopyAppSettings>(),
                isAddingCompleted: () => chunksToBatchForUpload.IsAddingCompleted, //B802
                batchForUpload: (b) => batchesToUpload.Add(b), //B804
                done: () => batchesToUpload.CompleteAdding()); //B810
            var createUploadBatchTask = createUploadBatchBlock.GetTask;

            
            var uploadBatchBlock = new UploadBatchBlock(
                logger: loggerFactory.CreateLogger<UploadBatchBlock>(),
                source: batchesToUpload,
                maxDegreeOfParallelism: 1 /*2*/,
                repo: repo,
                tier: options.Tier,
                chunkUploaded: (h) => removeFromPendingUpload(h), //B901
                done: () => manifestsToCreate.CompleteAdding()); //B910
            var uploadBatchTask = uploadBatchBlock.GetTask;

            
            void removeFromPendingUpload(HashValue chunkHash)
            {
                // Remove the given chunkHash from the list of pending-for-upload chunks for every manifest

                //TODO kan het zijn dat nadat deze hash is verwijderd van de chunks in de chunksForManifest, er nadien nog een manifest wordt toegevoegd dat OOK wacht op die chunk en dus deadlocked?

                foreach (var manifest in chunksForManifest.ToArray()) // ToArray() since we're modifying the collection in the for loop. See last paragraph of https://stackoverflow.com/a/65428882/1582323
                {
                    manifest.Value.PendingUpload.Remove(chunkHash);

                    if (!manifest.Value.PendingUpload.Any())
                    {
                        //All chunks for this manifest are now uploaded
                        if (!chunksForManifest.TryRemove(manifest.Key, out _))
                            throw new InvalidOperationException($"Manifest '{manifest.Key}'should have been present in the {nameof(chunksForManifest)} list but isn't");

                        manifestsToCreate.Add((ManifestHash: manifest.Key, ChunkHashes: manifest.Value.All)); //B1001
                    }
                }
            };


            var createManifestBlock = new CreateManifestBlock(
                logger: loggerFactory.CreateLogger<CreateManifestBlock>(),
                source: manifestsToCreate,
                maxDegreeOfParallelism: 1 /*2*/,
                repo: repo,
                manifestCreated: (manifestHash) =>
                {
                    if (!binariesWaitingForManifestCreation.Remove(manifestHash, out var binaryFiles))
                        throw new InvalidOperationException($"Manifest '{manifestHash.ToShortString()}' should have been present in the {nameof(binariesWaitingForManifestCreation)} list but isn't");

                    pointersToCreate.AddFromEnumerable(binaryFiles.AsEnumerable(), false); //B1101
                },
                done: () => { });
            var createManifestTask = createManifestBlock.GetTask;

            
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                               // can be ignored since we'll be awaiting the pointersToCreate
            Task.WhenAll(processHashedBinaryTask, createManifestTask)
                .ContinueWith(_ => pointersToCreate.CompleteAdding()); //B1110 - these are the two only blocks that write to this blockingcollection. If these are both done, adding is completed.
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed


            var createPointerFileIfNotExistsBlock = new CreatePointerFileIfNotExistsBlock(
                logger: loggerFactory.CreateLogger<CreatePointerFileIfNotExistsBlock>(),
                source: pointersToCreate,
                maxDegreeOfParallelism: 1 /*2*/,
                pointerService: pointerFileService,
                succesfullyBackedUp: bf => binariesToDelete.Add(bf), //B1202
                pointerFileCreated: (pf) => pointerFileEntriesToCreate.Add(pf), //B1201
                done: () => 
                { 
                });
            var createPointerFileIfNotExistsTask = createPointerFileIfNotExistsBlock.GetTask;


#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            // can be ignored since we'll be awaiting the pointersToCreate
            Task.WhenAll(hashTask, createPointerFileIfNotExistsTask)
                .ContinueWith(_ => pointerFileEntriesToCreate.CompleteAdding()); //B1210 - these are the two only blocks that write to this blockingcollection. If these are both done, adding is completed.
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed


            var createPointerFileEntryIfNotExistsBlock = new CreatePointerFileEntryIfNotExistsBlock(
                logger: loggerFactory.CreateLogger<CreatePointerFileEntryIfNotExistsBlock>(),
                source: pointerFileEntriesToCreate,
                maxDegreeOfParallelism: 1 /*2*/,
                repo: repo,
                versionUtc: versionUtc,
                done: () =>
                {
                });
            var createPointerFileEntryIfNotExistsTask = createPointerFileEntryIfNotExistsBlock.GetTask;


#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            // can be ignored since we'll be awaiting the pointersToCreate
            Task.WhenAll(processHashedBinaryTask, createPointerFileIfNotExistsTask)
                .ContinueWith(_ => binariesToDelete.CompleteAdding()); //B1310
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed


            var deleteBinaryFilesBlock = new DeleteBinaryFilesBlock(
                logger: loggerFactory.CreateLogger<DeleteBinaryFilesBlock>(),
                source: binariesToDelete,
                maxDegreeOfParallelism: 1 /*2*/,
                removeLocal: options.RemoveLocal,
                done: () =>
                {
                });
            var deleteBinaryFilesTask = deleteBinaryFilesBlock.GetTask;


            var pointerFileEntriesToCheckForDeletedPointers = new BlockingCollection<PointerFileEntry>();
            var createDeletedPointerFileEntryForDeletedPointerFilesBlock = new CreateDeletedPointerFileEntryForDeletedPointerFilesBlock(
                logger: loggerFactory.CreateLogger<CreateDeletedPointerFileEntryForDeletedPointerFilesBlock>(),
                source: pointerFileEntriesToCheckForDeletedPointers,
                maxDegreeOfParallelism: 1 /*2*/,
                repo: repo,
                pointerService: pointerFileService,
                versionUtc: versionUtc,
                start: async () => 
                {
                    var pfes = (await repo.GetCurrentEntries(includeDeleted: true))
                                          .Where(e => e.VersionUtc < versionUtc); // that were not created in the current run (those are assumed to be up to date)
                    pointerFileEntriesToCheckForDeletedPointers.AddFromEnumerable(pfes, true); //B1401
                },
                done: () => { });
            var createDeletedPointerFileEntryForDeletedPointerFilesTask = createDeletedPointerFileEntryForDeletedPointerFilesBlock.GetTask;

            

            
            var pointerFileEntriesToExport = new BlockingCollection<PointerFileEntry>();
            var exportJsonBlock = new ExportToJsonBlock(
                logger: loggerFactory.CreateLogger<ExportToJsonBlock>(),
                source: pointerFileEntriesToExport,
                repo: repo,
                versionUtc: versionUtc,
                start: async () =>
                {
                    var pfes = await repo.GetCurrentEntries(includeDeleted: false);
                    pointerFileEntriesToExport.AddFromEnumerable(pfes, true); //B1501
                },
                done: () => { });
            var exportJsonTask = await createPointerFileEntryIfNotExistsTask.ContinueWith(async _ => 
            {
                await exportJsonBlock.GetTask; //B1502
            });


            //while (true)
            //{
            //    await Task.Yield();
            //}


            // Await the current stage of the pipeline
            await Task.WhenAny(Task.WhenAll(BlockBase.AllTasks), BlockBase.CancellationTask);

            await exportJsonTask;

            if (BlockBase.AllTasks.Where(t => t.Status == TaskStatus.Faulted) is var ts
                && ts.Any())
            {
                var exceptions = ts.Select(t => t.Exception);
                throw new AggregateException(exceptions);
            }

            return 0;
        }
    }
}
