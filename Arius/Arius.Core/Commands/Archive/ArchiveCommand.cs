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
using System.Net.Sockets;
using System.Threading.Tasks;
using Arius.Core.Services.Chunkers;
using System.Threading.Channels;
using Arius.Core.Extensions;

namespace Arius.Core.Commands.Archive;

internal class ArchiveCommand : ICommand
{
    public ArchiveCommand(ArchiveCommandOptions options,
        ILogger<ArchiveCommand> logger,
        IServiceProvider serviceProvider)
    {
        this.options = options;
        this.logger = logger;
        services = serviceProvider;
    }

    private readonly ArchiveCommandOptions options;
    private readonly ILogger<ArchiveCommand> logger;
    private readonly IServiceProvider services;

    IServiceProvider ICommand.Services => services;


    public async Task<int> Execute()
    {
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var repo = services.GetRequiredService<Repository>();
        var pointerService = services.GetRequiredService<PointerService>();
        

        var binariesToUpload = Channel.CreateBounded<BinaryFile>(new BoundedChannelOptions(options.BinariesToUpload_BufferSize) { FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false, SingleWriter = false, SingleReader = false });
        var pointerFileEntriesToCreate = Channel.CreateBounded<PointerFile>(new BoundedChannelOptions(options.PointerFileEntriesToCreate_BufferSize){  FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false, SingleWriter = false, SingleReader = false });
        var binariesToDelete = Channel.CreateBounded<BinaryFile>(new BoundedChannelOptions(options.BinariesToDelete_BufferSize) { FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false, SingleWriter = false, SingleReader = false });
        var binaryFileUploadCompleted = new TaskCompletionSource();

        var indexBlock = new IndexBlock(
            loggerFactory: loggerFactory,
            sourceFunc: () => options.Path,
            maxDegreeOfParallelism: options.IndexBlock_Parallelism,
            fastHash: options.FastHash,
            pointerService: pointerService,
            repo: repo,
            hvp: services.GetRequiredService<IHashValueProvider>(),
            onIndexedPointerFile: async pf =>
            {
                await pointerFileEntriesToCreate.Writer.WriteAsync(pf); //B301
            },
            onIndexedBinaryFile: async arg =>
            {
                var (bf, alreadyBackedUp) = arg;
                if (alreadyBackedUp)
                {
                    if (options.RemoveLocal)
                        throw new NotImplementedException(); //todo redundant with b1202?
                        //await binariesToDelete.Writer.WriteAsync(bf); //B401
                    //else - discard //B304
                }
                else
                    await binariesToUpload.Writer.WriteAsync(bf); //B302
            },
            onBinaryFileIndexCompleted: () =>
            {
                binariesToUpload.Writer.Complete(); //B310
            },
            binaryFileUploadCompletedTaskCompletionSource: binaryFileUploadCompleted,
            onCompleted: () => { });
        var indexTask = indexBlock.GetTask;



        var pointersToCreate = Channel.CreateBounded<BinaryFile>(new BoundedChannelOptions(options.PointersToCreate_BufferSize) { FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false, SingleWriter = false, SingleReader = false });

        var uploadBinaryFileBlock = new UploadBinaryFileBlock(
            loggerFactory: loggerFactory,
            sourceFunc: () => binariesToUpload,
            maxDegreeOfParallelism: options.UploadBinaryFileBlock_BinaryFileParallelism,
            repo: repo,
            options: options,
            onBinaryExists: async bf =>
            {
                await pointersToCreate.Writer.WriteAsync(bf); //B403
            },
            onCompleted: () =>
            {
                pointersToCreate.Writer.Complete(); //B410
                binaryFileUploadCompleted.SetResult(); //B411
            }
        );
        var uploadBinaryFileTask = uploadBinaryFileBlock.GetTask;


        
        var createPointerFileIfNotExistsBlock = new CreatePointerFileIfNotExistsBlock(
            loggerFactory: loggerFactory,
            sourceFunc: () => pointersToCreate,
            maxDegreeOfParallelism: options.CreatePointerFileIfNotExistsBlock_Parallelism,
            pointerService: pointerService,
            onSuccesfullyBackedUp: async bf =>
            {
                if (options.RemoveLocal)
                    await binariesToDelete.Writer.WriteAsync(bf); //B1202
            },
            onPointerFileCreated: async pf => await pointerFileEntriesToCreate.Writer.WriteAsync(pf), //B1201
            onCompleted: () => binariesToDelete.Writer.Complete() //B1310
            );
        var createPointerFileIfNotExistsTask = createPointerFileIfNotExistsBlock.GetTask;



#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        // can be ignored since we'll be awaiting the pointersToCreate
        Task.WhenAll(indexTask, createPointerFileIfNotExistsTask)
            .ContinueWith(_ => pointerFileEntriesToCreate.Writer.Complete()); //B1210 - these are the two only blocks that write to this blockingcollection. If these are both done, adding is completed.
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

        
        var createPointerFileEntryIfNotExistsBlock = new CreatePointerFileEntryIfNotExistsBlock(
            loggerFactory: loggerFactory,
            sourceFunc: () => pointerFileEntriesToCreate,
            maxDegreeOfParallelism: options.CreatePointerFileEntryIfNotExistsBlock_Parallelism,
            repo: repo,
            versionUtc: options.VersionUtc,
            onCompleted: () => { });
        var createPointerFileEntryIfNotExistsTask = createPointerFileEntryIfNotExistsBlock.GetTask;



        var deleteBinaryFilesBlock = new DeleteBinaryFilesBlock(
            loggerFactory: loggerFactory,
            sourceFunc: () => binariesToDelete,
            maxDegreeOfParallelism: options.DeleteBinaryFilesBlock_Parallelism,
            onCompleted: () => { });
        var deleteBinaryFilesTask = deleteBinaryFilesBlock.GetTask;



        var createDeletedPointerFileEntryForDeletedPointerFilesBlock = new CreateDeletedPointerFileEntryForDeletedPointerFilesBlock(
            loggerFactory: loggerFactory,
            sourceFunc: async () =>
            {
                var pointerFileEntriesToCheckForDeletedPointers = Channel.CreateUnbounded<PointerFileEntry>(new UnboundedChannelOptions() { AllowSynchronousContinuations = false, SingleWriter = true, SingleReader = false });
                var pfes = (await repo.PointerFileEntries.GetCurrentEntries(includeDeleted: false))
                    .Where(pfe => pfe.VersionUtc < options.VersionUtc); // that were not created in the current run (those are assumed to be up to date)
                await pointerFileEntriesToCheckForDeletedPointers.Writer.AddFromEnumerable(pfes, completeAddingWhenDone: true); //B1401
                return pointerFileEntriesToCheckForDeletedPointers;
            },
            maxDegreeOfParallelism: options.CreateDeletedPointerFileEntryForDeletedPointerFilesBlock_Parallelism,
            repo: repo,
            root: options.Path,
            pointerService: pointerService,
            versionUtc: options.VersionUtc,
            onCompleted: () => { });
        var createDeletedPointerFileEntryForDeletedPointerFilesTask = createDeletedPointerFileEntryForDeletedPointerFilesBlock.GetTask;



        //var commitPointerFileEntryRepositoryTask = Task.WhenAll(createPointerFileEntryIfNotExistsTask, createDeletedPointerFileEntryForDeletedPointerFilesTask)
        //    .ContinueWith(async _ => await repo.PointerFileEntries.CommitPointerFileEntries()); //B1502 //TODO also Commit in a finally clause?



        //var exportJsonBlock = new ExportToJsonBlock(
        //    loggerFactory: loggerFactory,
        //    sourceFunc: async () =>
        //    {
        //        await Task.WhenAll(createPointerFileEntryIfNotExistsTask, createDeletedPointerFileEntryForDeletedPointerFilesTask); //B1503 -- wait for the PointerFileEntries to be up to date

        //        var pointerFileEntriesToExport = Channel.CreateUnbounded<PointerFileEntry>(new UnboundedChannelOptions() { AllowSynchronousContinuations = false, SingleWriter = true, SingleReader = false });
        //        var pfes = await repo.PointerFileEntries.GetCurrentEntries(includeDeleted: false);
        //        await pointerFileEntriesToExport.Writer.AddFromEnumerable(pfes, true); //B1501
        //        return pointerFileEntriesToExport;
        //    },
        //    repo: repo,
        //    versionUtc: versionUtc,
        //    done: () => { });
        //var exportJsonTask = exportJsonBlock.GetTask; //B1502
        //var exportJsonTask = createPointerFileEntryIfNotExistsTask
        //    .ContinueWith(async _ => await exportJsonBlock.GetTask); //B1502

        var validateBlock = new ValidateBlock(
            loggerFactory: loggerFactory,
            sourceFunc: null,
            repo: repo,
            versionUtc: options.VersionUtc,
            done: () => { });


        var updateTierBlock = new UpdateTierBlock(
            loggerFactory: loggerFactory,
            sourceFunc: () => repo,
            maxDegreeOfParallelism: 10,
            repo: repo,
            targetAccessTier: options.Tier,
            onCompleted: () => { });
        var updateTierTask = updateTierBlock.GetTask;
            

        //while (true)
        //{
        //    await Task.Yield();
        //}



        // Await the current stage of the pipeline
        await Task.WhenAny(Task.WhenAll(BlockBase.AllTasks/*.Append(exportJsonTask)*//*.Append(commitPointerFileEntryRepositoryTask)*/), BlockBase.CancellationTask);

        if (BlockBase.AllTasks.Where(t => t.Status == TaskStatus.Faulted) is var ts
            && ts.Any())
        {
            var exceptions = ts.Select(t => t.Exception);

            foreach (var e in exceptions)
                logger.LogError(e);

            throw new AggregateException(exceptions);
        }

        await repo.States.CommitToBlobStorage(options.VersionUtc);
        //else if (!binaryFilesWaitingForManifestCreation.IsEmpty /*|| chunksForManifest.Count > 0*/)
        //{
        //    //something went wrong
        //    throw new InvalidOperationException("Not all queues are emptied");
        //}

        return 0;
    }
}