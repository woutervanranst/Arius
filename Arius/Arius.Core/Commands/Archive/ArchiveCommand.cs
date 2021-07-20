using Arius.Core.Configuration;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Services.Chunkers;

namespace Arius.Core.Commands.Archive
{
    internal class ArchiveCommand : ICommand
    {
        internal interface IOptions
        {
            bool FastHash { get; }
            bool Dedup { get; }
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
            services = serviceProvider;
        }

        private readonly IOptions options;
        private readonly ILogger<ArchiveCommand> logger;
        private readonly IServiceProvider services;

        IServiceProvider ICommand.Services => services;


        public async Task<int> Execute()
        {
            var loggerFactory = services.GetRequiredService<ILoggerFactory>();
            var repo = services.GetRequiredService<Repository>();
            var versionUtc = DateTime.Now.ToUniversalTime(); //  !! Table Storage bewaart alles in universal time TODO nadenken over andere impact TODO test dit
            var pointerService = services.GetRequiredService<PointerService>();
            var root = new DirectoryInfo(options.Path);


            var pointerFileEntriesToCreate = new BlockingCollection<PointerFile>();
            var binariesToProcess = new BlockingCollection<BinaryFile>();
            var binariesToDelete = new BlockingCollection<BinaryFile>();

            var indexBlock = new IndexBlock(
                logger: loggerFactory.CreateLogger<IndexBlock>(),
                sourceFunc: () => root,
                maxDegreeOfParallelism: 1 /*2*/ /*Environment.ProcessorCount */,
                fastHash: options.FastHash,
                pointerService: pointerService,
                repo: repo,
                indexedPointerFile: pf => pointerFileEntriesToCreate.Add(pf), //B301
                indexedBinaryFile: arg =>
                {
                    var (bf, alreadyBackedUp) = arg;
                    if (alreadyBackedUp)
                        binariesToDelete.Add(bf); //B401
                    else
                        binariesToProcess.Add(bf); //B302
                },
                hvp: services.GetRequiredService<IHashValueProvider>(),
                done: () => binariesToProcess.CompleteAdding()); //B310
            var indexTask = indexBlock.GetTask;


            var binariesToUpload = new BlockingCollection<BinaryFile>();
            var binaryFilesWaitingForManifestCreation = new ConcurrentDictionary<ManifestHash, ConcurrentBag<BinaryFile>>(); //Key: ManifestHash. Values (BinaryFiles) that are waiting for the Keys (Manifests) to be created
            var pointersToCreate = new BlockingCollection<BinaryFile>();

            var processBinaryFileBlock = new ProcessBinaryFileBlock(
                logger: loggerFactory.CreateLogger<ProcessBinaryFileBlock>(),
                sourceFunc: () => binariesToProcess,
                repo: repo,

                uploadBinaryFile: (bf) => binariesToUpload.Add(bf),  //B402
                manifestExists: (bf) => pointersToCreate.Add(bf), //B403
                waitForCreatedManifest: (bf) => //B404
                {
                    binaryFilesWaitingForManifestCreation.AddOrUpdate(
                        key: bf.Hash,
                        addValue: new() { bf },
                        updateValueFactory: (h, bag) =>
                        {
                            bag.Add(bf);
                            return bag;
                        });
                },
                done: () => binariesToUpload.CompleteAdding()); //B410
            var processBinaryFileTask = processBinaryFileBlock.GetTask;


            var manifestsToCreate = new BlockingCollection<(ManifestHash ManifestHash, ChunkHash[] ChunkHashes)>();
            //var chunksForManifest = new ConcurrentDictionary<ManifestHash, ChunkHash[]>();

            var uploadBinaryFileBlock = new UploadBinaryFileBlock(
                logger: loggerFactory.CreateLogger<UploadBinaryFileBlock>(),
                sourceFunc: () => binariesToUpload,
                maxDegreeOfParallelism: 16 /*16*/,
                chunker: services.GetRequiredService<ByteBoundaryChunker>(),
                hvp: services.GetRequiredService<IHashValueProvider>(),
                repo: repo,
                options: options,
                chunksForManifest: (mh, chs) =>
                {
                    manifestsToCreate.Add((mh, chs));
                    //chunksForManifest.AddOrUpdate(
                    //    key: mh,
                    //    addValue: chs, //Add the full list of chunks (for writing the manifest later)
                    //    updateValueFactory: (_, _) => throw new InvalidOperationException("This should not happen. Once a BinaryFile is emitted for chunking, the chunks should not be updated"));
                },
                done: () => 
                {
                    manifestsToCreate.CompleteAdding(); //B910
                });
            var uploadBinaryFileTask = uploadBinaryFileBlock.GetTask;




            //void removeFromPendingUpload(params ChunkHash[] chunkHash)
            //{
            //    // Remove the given chunkHash from the list of pending-for-upload chunks for every manifest

            //    //TODO kan het zijn dat nadat deze hash is verwijderd van de chunks in de chunksForManifest, er nadien nog een manifest wordt toegevoegd dat OOK wacht op die chunk en dus deadlocked?
            //};


            var createManifestBlock = new CreateManifestBlock(
                logger: loggerFactory.CreateLogger<CreateManifestBlock>(),
                sourceFunc: () => manifestsToCreate,
                maxDegreeOfParallelism: 1 /*2*/,
                repo: repo,
                manifestCreated: (manifestHash) =>
                {
                    if (!binaryFilesWaitingForManifestCreation.Remove(manifestHash, out var binaryFiles))
                        throw new InvalidOperationException($"Manifest '{manifestHash.ToShortString()}' should have been present in the {nameof(binaryFilesWaitingForManifestCreation)} list but isn't");

                    pointersToCreate.AddFromEnumerable(binaryFiles.AsEnumerable(), false); //B1101
                },
                done: () => { });
            var createManifestTask = createManifestBlock.GetTask;


#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            // can be ignored since we'll be awaiting the pointersToCreate
            Task.WhenAll(processBinaryFileTask, createManifestTask)
                .ContinueWith(_ => pointersToCreate.CompleteAdding()); //B1110 - these are the two only blocks that write to this blockingcollection. If these are both done, adding is completed.
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed


            var createPointerFileIfNotExistsBlock = new CreatePointerFileIfNotExistsBlock(
                logger: loggerFactory.CreateLogger<CreatePointerFileIfNotExistsBlock>(),
                sourceFunc: () => pointersToCreate,
                maxDegreeOfParallelism: 1 /*2*/,
                pointerService: pointerService,
                succesfullyBackedUp: bf => binariesToDelete.Add(bf), //B1202
                pointerFileCreated: (pf) => pointerFileEntriesToCreate.Add(pf), //B1201
                done: () =>
                {
                });
            var createPointerFileIfNotExistsTask = createPointerFileIfNotExistsBlock.GetTask;


#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            // can be ignored since we'll be awaiting the pointersToCreate
            Task.WhenAll(indexTask, createPointerFileIfNotExistsTask)
                .ContinueWith(_ => pointerFileEntriesToCreate.CompleteAdding()); //B1210 - these are the two only blocks that write to this blockingcollection. If these are both done, adding is completed.
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed


            var createPointerFileEntryIfNotExistsBlock = new CreatePointerFileEntryIfNotExistsBlock(
                logger: loggerFactory.CreateLogger<CreatePointerFileEntryIfNotExistsBlock>(),
                sourceFunc: () => pointerFileEntriesToCreate,
                maxDegreeOfParallelism: 1 /*2*/,
                repo: repo,
                versionUtc: versionUtc,
                done: () =>
                {
                });
            var createPointerFileEntryIfNotExistsTask = createPointerFileEntryIfNotExistsBlock.GetTask;


#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            // can be ignored since we'll be awaiting the pointersToCreate
            Task.WhenAll(processBinaryFileTask, createPointerFileIfNotExistsTask)
                .ContinueWith(_ => binariesToDelete.CompleteAdding()); //B1310
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed


            var deleteBinaryFilesBlock = new DeleteBinaryFilesBlock(
                logger: loggerFactory.CreateLogger<DeleteBinaryFilesBlock>(),
                sourceFunc: () => binariesToDelete,
                maxDegreeOfParallelism: 1 /*2*/,
                removeLocal: options.RemoveLocal,
                done: () =>
                {
                });
            var deleteBinaryFilesTask = deleteBinaryFilesBlock.GetTask;



            var createDeletedPointerFileEntryForDeletedPointerFilesBlock = new CreateDeletedPointerFileEntryForDeletedPointerFilesBlock(
                logger: loggerFactory.CreateLogger<CreateDeletedPointerFileEntryForDeletedPointerFilesBlock>(),
                sourceFunc: async () =>
                {
                    var pointerFileEntriesToCheckForDeletedPointers = new BlockingCollection<PointerFileEntry>();
                    var pfes = (await repo.GetCurrentEntries(includeDeleted: true))
                                          .Where(e => e.VersionUtc < versionUtc); // that were not created in the current run (those are assumed to be up to date)
                    pointerFileEntriesToCheckForDeletedPointers.AddFromEnumerable(pfes, true); //B1401
                    return pointerFileEntriesToCheckForDeletedPointers;
                },
                maxDegreeOfParallelism: 1 /*2*/,
                repo: repo,
                root: root,
                pointerService: pointerService,
                versionUtc: versionUtc,
                done: () => { });
            var createDeletedPointerFileEntryForDeletedPointerFilesTask = createDeletedPointerFileEntryForDeletedPointerFilesBlock.GetTask;



            var exportJsonBlock = new ExportToJsonBlock(
                logger: loggerFactory.CreateLogger<ExportToJsonBlock>(),
                sourceFunc: async () =>
                {
                    var pfes = await repo.GetCurrentEntries(includeDeleted: false);
                    var pointerFileEntriesToExport = new BlockingCollection<PointerFileEntry>();
                    pointerFileEntriesToExport.AddFromEnumerable(pfes, true); //B1501
                    return pointerFileEntriesToExport;
                },
                repo: repo,
                versionUtc: versionUtc,
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
            await Task.WhenAny(Task.WhenAll(BlockBase.AllTasks.Append(exportJsonTask)), BlockBase.CancellationTask);

            if (BlockBase.AllTasks.Where(t => t.Status == TaskStatus.Faulted) is var ts
                && ts.Any())
            {
                var exceptions = ts.Select(t => t.Exception);
                throw new AggregateException(exceptions);
            }
            else if (binaryFilesWaitingForManifestCreation.Count > 0 /*|| chunksForManifest.Count > 0*/)
            {
                //something went wrong
                throw new InvalidOperationException("Not all queues are emptied");
            }

            return 0;
        }
    }
}
