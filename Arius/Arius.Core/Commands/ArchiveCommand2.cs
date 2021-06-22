using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
            string Path { get; }
        }

        public ArchiveCommand2(IOptions options,
            ILogger<ArchiveCommand> logger,
            IServiceProvider serviceProvider)
        {
            root = new DirectoryInfo(options.Path);
            this._logger = logger;
            this.services = serviceProvider;
        }

        private readonly DirectoryInfo root;
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
                root: root,
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
                    createManifest.CompleteAdding();
                    //createPointerFileEntry.CompleteAdding(); HIER NIET
                });
            var hashTask = hashBlock.GetTask;


            var binariesToChunk = new BlockingCollection<BinaryFile>();
            var binariesWaitingForManifests = new ConcurrentDictionary<HashValue, ConcurrentBag<BinaryFile>>(); //Key: ManifestHash. Values (BinaryFiles) that are waiting for the Keys (Manifests) to be created
            var pointersToCreate = new BlockingCollectionEx<BinaryFile>();

            var processHashedBinaryBlock = new ProcessHashedBinaryBlock(
                logger: services.GetRequiredService<ILoggerFactory>().CreateLogger<ProcessHashedBinaryBlock>(),
                //continueWhile: () => !createManifest.IsCompleted,
                source: createManifest.GetConsumingEnumerable(),
                repo: services.GetRequiredService<AzureRepository>(),
                uploadBinaryFile: (bf) => binariesToChunk.Add(bf),  //B401
                waitForCreatedManifest: (bf) =>
                {
                    binariesWaitingForManifests.AddOrUpdate(
                        key: bf.Hash,
                        addValue: new() { bf },
                        updateValueFactory: (h, bag) =>
                        {
                            bag.Add(bf);
                            return bag;
                        });
                }, //B402
                manifestExists: (bf) => pointersToCreate.Add(bf), //B403
                done: () =>
                {
                    binariesToChunk.CompleteAdding(); //B410
                    //pointersToCreate.CompleteAdding(); NIET HIER

                });
            var processHashedBinaryTask = processHashedBinaryBlock.GetTask;


            var chunksToProcess = new BlockingCollection<IChunkFile>();
            var chunksForManifest = new ConcurrentDictionary<HashValue, (HashValue[] All, List<HashValue> PendingUpload)>(); //Key: ManifestHash, Value: ChunkHashes. 

            var chunkBlock = new ChunkBlock(
                logger: services.GetRequiredService<ILoggerFactory>().CreateLogger<ChunkBlock>(),
                source: binariesToChunk.GetConsumingPartitioner(),
                maxDegreeOfParallelism: 1 /*2*/,
                chunker: services.GetRequiredService<IChunker>(),
                chunkedBinary: (bf, cfs) => 
                {
                    //B501
                    chunksToProcess.AddFromEnumerable(cfs, false);

                    //B502
                    var chs = cfs.Select(ch => ch.Hash).ToArray();
                    chunksForManifest.AddOrUpdate( 
                        key: bf.Hash,
                        addValue: (All: chs, PendingUpload: chs.ToList()), //Add the full list of chunks (for writing the manifest later) and a modifyable list of chunks (for reconciliation upon upload for triggering manifest creation)
                        updateValueFactory: (_, _) => throw new InvalidOperationException("This should not happen. Once a BinaryFile is emitted for chunking, the chunks should not be updated"));
                },
                done: () =>
                {
                    
                });
            var chunkTask = chunkBlock.GetTask;


            var chunksToUpload = new BlockingCollection<IChunkFile>();
            //var chunkWaitList = new ConcurrentDictionary<HashValue, List<HashValue>>(); //Key: ManifestHash, Value: bag of ChunkHash

            var processChunkBlock = new ProcessChunkBlock(
                logger: services.GetRequiredService<ILoggerFactory>().CreateLogger<ProcessChunkBlock>(),
                source: chunksToProcess.GetConsumingEnumerable(),
                repo: services.GetRequiredService<AzureRepository>(),

                uploadChunkFile: (cf) => chunksToUpload.Add(cf), //B601
                chunkAlreadyUploaded: (h) => removeFromPendingUpload(h),
                done: () =>
                {
                    //chunksToUpload.CompleteAdding(); //B503
                });
            var processChunkTask = processChunkBlock.GetTask;


            void removeFromPendingUpload(HashValue h)
            {
                foreach (var item in chunksForManifest.Values)
                {
                    item.PendingUpload.Remove(h);

                    if (!item.PendingUpload.Any())
                    {
                        // all chunks are now uploaded

                    }
                }
            };



            await Task.WhenAll(pointersToCreate.WaitAddingCompleted);

            await Task.WhenAll(BlockBase.AllTasks);

            return 0;
        }
    }
    

    
}
