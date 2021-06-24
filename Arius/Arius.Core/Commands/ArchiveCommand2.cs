using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Arius.Core.Commands
{
    internal class ArchiveCommand2 : ICommand
    {
        internal interface IOptions
        {
            AccessTier Tier { get; }
            string Path { get; }
        }

        public ArchiveCommand2(IOptions options,
            ILogger<ArchiveCommand> logger,
            IServiceProvider serviceProvider)
        {
            this.options = options;
            this._logger = logger;
            this.services = serviceProvider;
        }

        private readonly IOptions options;
        private readonly ILogger<ArchiveCommand> _logger;
        private readonly IServiceProvider services;

        internal static void AddBlockProviders(IServiceCollection coll/*, Facade.Facade.ArchiveCommandOptions options*/)
        {
        }

        IServiceProvider ICommand.Services => throw new NotImplementedException();


        public async Task<int> Execute()
        {
            var indexedFiles = new BlockingCollection<IFile>();

            var indexBlock = new IndexBlock(
                logger: services.GetRequiredService<ILoggerFactory>().CreateLogger<IndexBlock>(),
                root: new DirectoryInfo(options.Path),
                indexedFile: (file) => indexedFiles.Add(file),
                done: () => indexedFiles.CompleteAdding());
            var indexTask = indexBlock.GetTask;


            var createPointerFileEntry = new BlockingCollection<PointerFile>();
            var createManifest = new BlockingCollection<BinaryFile>();

            var hashBlock = new HashBlock(
                logger: services.GetRequiredService<ILoggerFactory>().CreateLogger<HashBlock>(),
                //continueWhile: () => !indexedFiles.IsCompleted,
                source: indexedFiles.GetConsumingPartitioner(),
                maxDegreeOfParallelism: 1 /*2*/ /*Environment.ProcessorCount */,
                hashedPointerFile: (pf) => createPointerFileEntry.Add(pf),
                hashedBinaryFile: (bf) => createManifest.Add(bf),
                hvp: services.GetRequiredService<IHashValueProvider>(),
                done: () =>
                {
                    createManifest.CompleteAdding(); //B310
                    //createPointerFileEntry.CompleteAdding(); HIER NIET
                });
            var hashTask = hashBlock.GetTask;


            var binariesToChunk = new BlockingCollection<BinaryFile>();
            var binariesWaitingForManifestCreation = new ConcurrentDictionary<HashValue, ConcurrentBag<BinaryFile>>(); //Key: ManifestHash. Values (BinaryFiles) that are waiting for the Keys (Manifests) to be created
            var pointersToCreate = new BlockingCollection<BinaryFile>();

            var processHashedBinaryBlock = new ProcessHashedBinaryBlock(
                logger: services.GetRequiredService<ILoggerFactory>().CreateLogger<ProcessHashedBinaryBlock>(),
                //continueWhile: () => !createManifest.IsCompleted,
                source: createManifest.GetConsumingEnumerable(),
                repo: services.GetRequiredService<AzureRepository>(),
                uploadBinaryFile: (bf) => binariesToChunk.Add(bf),  //B401
                waitForCreatedManifest: (bf) => //B402
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
                manifestExists: (bf) => pointersToCreate.Add(bf), //B403
                done: () =>
                {
                    binariesToChunk.CompleteAdding(); //B410
                    //pointersToCreate.CompleteAdding(); NIET HIER

                });
            var processHashedBinaryTask = processHashedBinaryBlock.GetTask;

            //await Task.Delay(10000);


            var chunksToProcess = new BlockingCollection<IChunkFile>();
            var chunksForManifest = new ConcurrentDictionary<HashValue, (HashValue[] All, List<HashValue> PendingUpload)>(); //Key: ManifestHash, Value: ChunkHashes. 

            var chunkBlock = new ChunkBlock(
                logger: services.GetRequiredService<ILoggerFactory>().CreateLogger<ChunkBlock>(),
                source: binariesToChunk.GetConsumingPartitioner(),
                maxDegreeOfParallelism: 1 /*2*/,
                chunker: services.GetRequiredService<IChunker>(),
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
                done: () =>
                {
                    chunksToProcess.CompleteAdding(); //B510
                });
            var chunkTask = chunkBlock.GetTask;


            var chunksToEncrypt = new BlockingCollection<IChunkFile>();
            var manifestsToCreate = new BlockingCollection<(HashValue ManifestHash, HashValue[] ChunkHashes)>();

            var processChunkBlock = new ProcessChunkBlock(
                logger: services.GetRequiredService<ILoggerFactory>().CreateLogger<ProcessChunkBlock>(),
                source: chunksToProcess.GetConsumingEnumerable(),
                repo: services.GetRequiredService<AzureRepository>(),
                chunkToUpload: (cf) => chunksToEncrypt.Add(cf), //B601
                chunkAlreadyUploaded: (h) => removeFromPendingUpload(h), //B602
                done: () =>
                {
                    chunksToEncrypt.CompleteAdding(); //B610
                });
            var processChunkTask = processChunkBlock.GetTask;


            var chunksToUpload = new BlockingCollection<EncryptedChunkFile>();

            var encryptChunkBlock = new EncryptChunkBlock(
                logger: services.GetRequiredService<ILoggerFactory>().CreateLogger<EncryptChunkBlock>(),
                source: chunksToEncrypt.GetConsumingPartitioner(),
                maxDegreeOfParallelism: 1 /*2*/,
                tempDirAppSettings: services.GetRequiredService<TempDirectoryAppSettings>(),
                encrypter: services.GetRequiredService<IEncrypter>(),
                chunkEncrypted: (ecf) => chunksToUpload.Add(ecf), //B701
                done: () =>
                {
                    chunksToUpload.CompleteAdding(); //B710
                });
            var encyptChunkBlockTask = encryptChunkBlock.GetTask;


            var batchesForUpload = new BlockingCollection<EncryptedChunkFile[]>();

            var createUploadBatchBlock = new CreateUploadBatchBlock(
                logger: services.GetRequiredService<ILoggerFactory>().CreateLogger<CreateUploadBatchBlock>(),
                source: chunksToUpload,
                azCopyAppSettings: services.GetRequiredService<AzCopyAppSettings>(),
                isAddingCompleted: () => chunksToUpload.IsAddingCompleted, //B802
                batchForUpload: (b) => batchesForUpload.Add(b), //B804
                done: () =>
                {
                    batchesForUpload.CompleteAdding(); //B810
                });
            var createUploadBatchTask = createUploadBatchBlock.GetTask;

            
            var uploadEncryptedChunkBlock = new UploadEncryptedChunkBlock(
                logger: services.GetRequiredService<ILoggerFactory>().CreateLogger<UploadEncryptedChunkBlock>(),
                source: batchesForUpload.GetConsumingPartitioner(),
                maxDegreeOfParallelism: 1 /*2*/,
                repo: services.GetRequiredService<AzureRepository>(),
                tier: options.Tier,
                chunkUploaded: (h) => removeFromPendingUpload(h), //B901
                done: () =>
                {
                    manifestsToCreate.CompleteAdding(); //B910
                });
            var uploadEncryptedChunkTask = uploadEncryptedChunkBlock.GetTask;

            
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
                logger: services.GetRequiredService<ILoggerFactory>().CreateLogger<CreateManifestBlock>(),
                source: manifestsToCreate.GetConsumingPartitioner(),
                maxDegreeOfParallelism: 1 /*2*/,
                repo: services.GetRequiredService<AzureRepository>(),
                manifestCreated: (manifestHash) => onManifestCreated(manifestHash), //B1101
                done: () =>
                {
                    throw new NotImplementedException();
                });
            var createManifestTask = createManifestBlock.GetTask;

            
            void onManifestCreated(HashValue manifestHash)
            {
                if (!binariesWaitingForManifestCreation.Remove(manifestHash, out var binaryFiles))
                    throw new InvalidOperationException($"Manifest '{manifestHash.ToShortString()}' should have been present in the {nameof(binariesWaitingForManifestCreation)} list but isn't");

                pointersToCreate.AddFromEnumerable(binaryFiles.AsEnumerable(), false); //B1201
            }


            await Task.WhenAll(processHashedBinaryBlock.GetTask, createManifestBlock.GetTask)
                .ContinueWith(_ => pointersToCreate.CompleteAdding()); //B1301 - these are the two only blocks that write to this blockingcollection. If these are both done, adding is completed.



            //await Task.WhenAll(batchesForUpload.is)

            while (true)
            {
                await Task.Yield();
            }


            await Task.WhenAll(BlockBase.AllTasks);

            return 0;
        }
    }
    

    
}
