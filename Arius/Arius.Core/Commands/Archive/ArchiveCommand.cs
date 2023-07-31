using Arius.Core.Models;
using Arius.Core.Repositories;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Channels;
using Arius.Core.Extensions;
using FluentValidation;
using FluentValidation.Results;

namespace Arius.Core.Commands.Archive;

internal partial class ArchiveCommand : ICommand<IArchiveCommandOptions> //This class is internal but the interface is public for use in the Facade
{
    public ArchiveCommand(ILoggerFactory loggerFactory, ILogger<ArchiveCommand> logger, 
        ArchiveCommandStatistics statisticsProvider)
    {
        this.loggerFactory = loggerFactory;
        this.logger = logger;
        this.stats = statisticsProvider;
    }

    private readonly ILoggerFactory                                   loggerFactory;
    private readonly ILogger<ArchiveCommand>                          logger;
    private readonly ArchiveCommandStatistics                         stats;
    private          ExecutionServiceProvider<IArchiveCommandOptions> executionServices;

    IServiceProvider ICommand<IArchiveCommandOptions>.Services => executionServices.Services;

    public ValidationResult Validate(IArchiveCommandOptions options)
    {
        var validator = new IArchiveCommandOptions.Validator();
        return validator.Validate(options);
    }
    
    public async Task<int> ExecuteAsync(IArchiveCommandOptions options)
    {
        var v = Validate(options);
        if (!v.IsValid)
            throw new ValidationException(v.Errors);

        executionServices = ExecutionServiceProvider<IArchiveCommandOptions>.BuildServiceProvider(loggerFactory, options);
        var repo = executionServices.GetRequiredService<Repository>();

        var binariesToUpload = Channel.CreateBounded<BinaryFile>(new BoundedChannelOptions(options.BinariesToUpload_BufferSize) { FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false, SingleWriter = false, SingleReader = false });
        var pointerFileEntriesToCreate = Channel.CreateBounded<PointerFile>(new BoundedChannelOptions(options.PointerFileEntriesToCreate_BufferSize){  FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false, SingleWriter = false, SingleReader = false });
        var binariesToDelete = Channel.CreateBounded<BinaryFile>(new BoundedChannelOptions(options.BinariesToDelete_BufferSize) { FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false, SingleWriter = false, SingleReader = false });
        var binaryFileUploadCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);


        // Get statistics of before the run
        var startStats = await repo.GetStats();
        stats.AddRemoteRepositoryStatistic(
            beforeBinaries: startStats.binaryCount,
            beforeSize: startStats.binariesSize,
            beforePointerFileEntries: startStats.currentPointerFileEntryCount);


        var indexBlock = new IndexBlock(this,
            sourceFunc: () => options.Path,
            maxDegreeOfParallelism: options.IndexBlock_Parallelism,
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
                        await binariesToDelete.Writer.WriteAsync(bf); //B401 //NOTE B1202 deletes NEWLY archived binaries, this one deletes EXISTING binaries //TODO test the flow
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
        var uploadBinaryFileBlock = new UploadBinaryFileBlock(this,
            sourceFunc: () => binariesToUpload,
            maxDegreeOfParallelism: options.UploadBinaryFileBlock_BinaryFileParallelism,
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

        
        var createPointerFileIfNotExistsBlock = new CreatePointerFileIfNotExistsBlock(this,
            sourceFunc: () => pointersToCreate,
            maxDegreeOfParallelism: options.CreatePointerFileIfNotExistsBlock_Parallelism,
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

        
        var createPointerFileEntryIfNotExistsBlock = new CreatePointerFileEntryIfNotExistsBlock(this,
            sourceFunc: () => pointerFileEntriesToCreate,
            maxDegreeOfParallelism: options.CreatePointerFileEntryIfNotExistsBlock_Parallelism,
            onCompleted: () => { });
        var createPointerFileEntryIfNotExistsTask = createPointerFileEntryIfNotExistsBlock.GetTask;


        var deleteBinaryFilesBlock = new DeleteBinaryFilesBlock(this,
            sourceFunc: () => binariesToDelete,
            maxDegreeOfParallelism: options.DeleteBinaryFilesBlock_Parallelism,
            onCompleted: () => { });
        var deleteBinaryFilesTask = deleteBinaryFilesBlock.GetTask;


        var createDeletedPointerFileEntryForDeletedPointerFilesBlock = new CreateDeletedPointerFileEntryForDeletedPointerFilesBlock(this,
            sourceFunc: async () =>
            {
                var pointerFileEntriesToCheckForDeletedPointers = Channel.CreateUnbounded<PointerFileEntry>(new UnboundedChannelOptions() { AllowSynchronousContinuations = false, SingleWriter = true, SingleReader = false });
                var pfes = (await repo.PointerFileEntries.GetCurrentEntriesAsync(includeDeleted: false))
                    .Where(pfe => pfe.VersionUtc < options.VersionUtc); // that were not created in the current run (those are assumed to be up to date)
                await pointerFileEntriesToCheckForDeletedPointers.Writer.AddFromEnumerable(pfes, completeAddingWhenDone: true); //B1401
                return pointerFileEntriesToCheckForDeletedPointers;
            },
            maxDegreeOfParallelism: options.CreateDeletedPointerFileEntryForDeletedPointerFilesBlock_Parallelism,
            root: options.Path,
            versionUtc: options.VersionUtc,
            onCompleted: () => { });
        var createDeletedPointerFileEntryForDeletedPointerFilesTask = createDeletedPointerFileEntryForDeletedPointerFilesBlock.GetTask;


        var updateTierBlock = new UpdateTierBlock(this,
            sourceFunc: () => repo,
            maxDegreeOfParallelism: options.UpdateTierBlock_Parallelism,
            onCompleted: () => { });
        var updateTierTask = updateTierBlock.GetTask;


        //while (true)
        //{
        //    var status = indexBlock.GetTask.Status != TaskStatus.RanToCompletion ? 
        //        "Indexing ongoing" :
        //        binariesToUpload.Reader.)

        //    await Task.Yield();
        //}


        // Await the current stage of the pipeline
        await Task.WhenAny(Task.WhenAll(BlockBase.AllTasks), BlockBase.CancellationTask);


        // Get statistics after the run
        var endStats = await repo.GetStats();
        stats.AddRemoteRepositoryStatistic(
            afterBinaries: endStats.binaryCount,
            afterSize: endStats.binariesSize,
            afterPointerFileEntries: endStats.currentPointerFileEntryCount);
        var vs = (await repo.PointerFileEntries.GetVersionsAsync()).ToArray();
        stats.versionCount = vs.Length;
        stats.lastVersion = vs.Last();

        // save the state in any case even in case of errors otherwise the info on BinaryProperties is lost
        await repo.States.CommitToBlobStorageAsync(options.VersionUtc);

        if (BlockBase.AllTasks.Where(t => t.Status == TaskStatus.Faulted) is var ts
            && ts.Any())
        {
            var exceptions = ts.Select(t => t.Exception);

            foreach (var e in exceptions)
                logger.LogError(e);

            throw new AggregateException(exceptions);
        }

        return 0;
    }
}