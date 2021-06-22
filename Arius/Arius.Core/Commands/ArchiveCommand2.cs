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
                maxDegreeOfParallelism: 2 /*Environment.ProcessorCount */,
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
            //var pointersToCreateDone = new AsyncManualResetEvent(); // new Mutex(); // SemaphoreSlim(1);

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
                    //pointersToCreateDone.Set();
                    //pointersToCreate.CompleteAdding(); NIET HIER

                });
            var processHashedBinaryTask = processHashedBinaryBlock.GetTask;


            //var chunkWaitList = new ConcurrentDictionary<HashValue, ConcurrentDictionary<HashValue, bool>>(); //Key: ManifestHash, Value: bag of ChunkHash
            var manifestsWaitingForChunks = new ConcurrentDictionary<HashValue, HashValue[]>(); //Key: ManifestHash, Value: ChunkHashes
            var chunksToUpload = new BlockingCollection<IChunkFile>();

            var chunkBlock = new ChunkBlock(
                logger: services.GetRequiredService<ILoggerFactory>().CreateLogger<ChunkBlock>(),
                //continueWhile: () => !binariesToChunk.IsCompleted,
                source: binariesToChunk.GetConsumingPartitioner(),
                maxDegreeOfParallelism: 2,
                chunker: services.GetRequiredService<IChunker>(),
                azureRepository: services.GetRequiredService<AzureRepository>(),
                chunkedBinary: (bf, cfs) => manifestsWaitingForChunks.AddOrUpdate(
                    key: bf.Hash,
                    addValue: cfs.Select(ch => ch.Hash).ToArray(),
                    updateValueFactory: (_, _) => throw new InvalidOperationException("This should not happen. Once a BinaryFile is emitted for chunking, the chunks should not be updated")), //B501
                uploadChunkFile: (cf) => chunksToUpload.Add(cf), //B502
                done: () =>
                {
                    chunksToUpload.CompleteAdding(); //B503
                });
            var chunkTask = chunkBlock.GetTask;



            await Task.WhenAll(pointersToCreate.WaitAddingCompleted);

            await Task.WhenAll(BlockBase.AllTasks);

            return 0;
        }
    }
    

    
}
